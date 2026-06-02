using System;
using Sandbox;

namespace RedSnail.WaterTool;

// Feeds the scene terrain's heightmap to the water shader so wave displacement can be
// damped in shallow water near shore. No-op (waves unchanged) when there is no terrain.
public static class WaterShoreDamping
{
	// Water depth (units) over which waves ramp from flat at the shoreline to full height.
	// 0 disables the effect. Tune live in console: water_shore_range <units>
	[ConVar( "water_shore_range" )]
	public static float ShoreRange { get; set; } = 2000f;

	// Max wave multiplier in deep water (1 = no boost). Tune: water_deep_boost <x>
	[ConVar( "water_deep_boost" )]
	public static float DeepWaveBoost { get; set; } = 1.5f;

	// Extra depth (beyond ShoreRange) over which waves ramp up to DeepWaveBoost.
	[ConVar( "water_deep_range" )]
	public static float DeepWaveRange { get; set; } = 2000f;

	private static Terrain _terrain;
	private static Texture _heightTex;
	private static int _builtResolution;

	public static void Apply( Scene scene, RenderAttributes attr )
	{
		var terrain = ShoreRange > 0f ? FindTerrain( scene ) : null;
		if ( terrain is null )
		{
			attr.Set( "ShoreWaveRange", 0f );
			return;
		}

		EnsureTexture( terrain );
		if ( _heightTex is null )
		{
			attr.Set( "ShoreWaveRange", 0f );
			return;
		}

		var storage = terrain.Storage;
		attr.Set( "TerrainHeightMap", _heightTex );
		attr.Set( "TerrainWorldMin", (Vector2)terrain.WorldPosition );
		attr.Set( "TerrainSize", storage.TerrainSize );
		attr.Set( "TerrainHeightScale", storage.TerrainHeight );
		attr.Set( "TerrainBaseZ", terrain.WorldPosition.z );
		attr.Set( "ShoreWaveRange", ShoreRange );
		attr.Set( "DeepWaveBoost", DeepWaveBoost );
		attr.Set( "DeepWaveRange", DeepWaveRange );
	}

	private static Terrain FindTerrain( Scene scene )
	{
		if ( _terrain.IsValid() && _terrain.Storage?.HeightMap is not null )
			return _terrain;

		foreach ( var t in scene.GetAllComponents<Terrain>() )
		{
			if ( t.IsValid() && t.Storage?.HeightMap is not null && t.Storage.Resolution > 1 )
			{
				_terrain = t;
				_builtResolution = 0;
				return t;
			}
		}

		return null;
	}

	private static void EnsureTexture( Terrain terrain )
	{
		var storage = terrain.Storage;
		int res = storage.Resolution;

		if ( _heightTex is not null && _builtResolution == res )
			return;

		var hm = storage.HeightMap;
		var norm = new float[res * res];
		for ( int i = 0; i < norm.Length && i < hm.Length; i++ )
			norm[i] = hm[i] / (float)ushort.MaxValue;

		var bytes = new byte[norm.Length * 4];
		Buffer.BlockCopy( norm, 0, bytes, 0, bytes.Length );

		_heightTex = Texture.Create( res, res )
			.WithFormat( ImageFormat.R32F )
			.WithData( bytes )
			.Finish();

		_builtResolution = res;
	}

	// CPU mirror of the shader's shore factor, so buoyancy reacts to the same (damped)
	// waves the player sees instead of bobbing on invisible full-height waves near shore.
	public static float ShoreFactor( Scene scene, Vector3 worldPoint, float waterZ )
	{
		if ( ShoreRange <= 0f || scene is null )
			return 1f;

		var terrain = FindTerrain( scene );
		if ( terrain is null || !TrySampleTerrainDepth( terrain, worldPoint, waterZ, out float localDepth, out bool insideBounds ) )
			return DeepWaveBoost;

		if ( !insideBounds )
			return DeepWaveBoost;

		float baseFactor = SmoothStep( 0f, ShoreRange, localDepth );
		float deep = DeepWaveRange > 0f ? Saturate( (localDepth - ShoreRange) / DeepWaveRange ) : 0f;
		return baseFactor + deep * ( DeepWaveBoost - 1f );
	}

	private static bool TrySampleTerrainDepth( Terrain terrain, Vector3 worldPoint, float waterZ, out float localDepth, out bool insideBounds )
	{
		localDepth = 0f;
		insideBounds = false;

		var storage = terrain.Storage;
		if ( storage?.HeightMap is null || storage.Resolution <= 1 )
			return false;

		var local = terrain.WorldTransform.PointToLocal( worldPoint );
		float size = storage.TerrainSize;
		insideBounds = local.x >= 0f && local.y >= 0f && local.x <= size && local.y <= size;

		int res = storage.Resolution;
		float gx = Math.Clamp( (local.x / size) * (res - 1), 0f, (float)(res - 1) );
		float gy = Math.Clamp( (local.y / size) * (res - 1), 0f, (float)(res - 1) );
		int x0 = (int)MathF.Floor( gx ), y0 = (int)MathF.Floor( gy );
		int x1 = Math.Min( x0 + 1, res - 1 ), y1 = Math.Min( y0 + 1, res - 1 );
		float tx = gx - x0, ty = gy - y0;

		var hm = storage.HeightMap;
		float h00 = hm[x0 + y0 * res], h10 = hm[x1 + y0 * res];
		float h01 = hm[x0 + y1 * res], h11 = hm[x1 + y1 * res];
		float h = MathX.Lerp( MathX.Lerp( h00, h10, tx ), MathX.Lerp( h01, h11, tx ), ty ) * ( storage.TerrainHeight / (float)ushort.MaxValue );

		float terrainWorldZ = terrain.WorldTransform.PointToWorld( new Vector3( local.x, local.y, h ) ).z;
		localDepth = waterZ - terrainWorldZ;
		return true;
	}

	private static float SmoothStep( float edge0, float edge1, float x )
	{
		if ( edge1 <= edge0 )
			return x >= edge1 ? 1f : 0f;

		float t = Math.Clamp( (x - edge0) / (edge1 - edge0), 0f, 1f );
		return t * t * (3f - 2f * t);
	}

	private static float Saturate( float x ) => Math.Clamp( x, 0f, 1f );
}
