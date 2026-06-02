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
}
