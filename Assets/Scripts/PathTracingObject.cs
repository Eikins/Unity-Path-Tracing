using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PathTracing
{
	public enum PathTracingObjectType
	{
		MESH,
		PLANE,
		SPHERE
	}

	[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
	public class PathTracingObject : MonoBehaviour
	{
		private PathTracingMaterial _material = PathTracingMaterial.Default;
		[SerializeField] private PathTracingObjectType _type;
		private bool _isValid;

		public PathTracingObjectType Type => _type;
		public PathTracingMaterial Material => _material;
		public bool IsValid => _isValid;

		private void Awake()
		{
			//InitObjectType();
			InitObjectMaterial();
		}

		void OnEnable()
		{
			PathTracingMaster.RegisterObject(this);
		}

		void OnDisable()
		{
			PathTracingMaster.UnregisterObject(this);
		}

		private void InitObjectType()
		{
			var meshName = GetComponent<MeshFilter>().sharedMesh.name;
			if (meshName == "Plane")
			{
				_type = PathTracingObjectType.PLANE;
			}
			else if (meshName == "Sphere")
			{
				_type = PathTracingObjectType.SPHERE;
			}
			else
			{
				_type = PathTracingObjectType.MESH;
			}
		}

		private void InitObjectMaterial()
		{
			_isValid = true;
			Material material = GetComponent<MeshRenderer>().sharedMaterial;
			Color albedo = material.GetColor("_Color");
			Color specular = material.GetColor("_SpecColor");
			Color emission = material.GetColor("_EmissionColor");
			_material.albedo = new Vector3(albedo.r, albedo.g, albedo.b);
			_material.specular = new Vector3(specular.r, specular.g, specular.b);
			_material.emission = new Vector3(emission.r, emission.g, emission.b);
			_material.smoothness = material.GetFloat("_Glossiness");
		}
	}

}