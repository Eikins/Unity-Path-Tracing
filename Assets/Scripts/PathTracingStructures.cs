using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PathTracing
{
	[System.Serializable]
	public struct PathTracingPlane
	{
		public Vector3 normal;
		public float offset;
		public uint materialIndex;

		public static readonly int Stride = sizeof(float) * 4 + sizeof(uint);
	}

	[System.Serializable]
	public struct PathTracingSphere
	{
		public Vector3 position;
		public float radius;
		public uint materialIndex;

		public static readonly int Stride = sizeof(float) * 4 + sizeof(uint);
	}

	[System.Serializable]
	public struct PathTracingMesh
	{
		public Matrix4x4 localToWorld;
		public uint indicesOffset;
		public uint indicesCount;
		public uint materialIndex;

		public static readonly int Stride = sizeof(float) * 16 + sizeof(int) * 3;
	}

	[System.Serializable]
	public struct PathTracingMaterial
	{
		public Vector3 albedo;
		public Vector3 specular;
		public Vector3 emission;
		public float smoothness;

		public static PathTracingMaterial Default => new PathTracingMaterial
		{
			albedo = new Vector3(0.5f, 0.5f, 0.5f),
			specular = Vector3.zero,
			emission = Vector3.zero,
			smoothness = 0f
		};

		public static readonly int Stride = sizeof(float) * 10;
	}
}
