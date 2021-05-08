using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PathTracing
{
	[RequireComponent(typeof(Camera))]
	public class PathTracingMaster : MonoBehaviour
	{
		#region Static Objects
		private static bool _pathTracingDirty = false;
		private static List<PathTracingObject> _pathTracingObjects = new List<PathTracingObject>();

		public static void RegisterObject(PathTracingObject obj)
		{
			_pathTracingObjects.Add(obj);
			_pathTracingDirty = true;
		}

		public static void UnregisterObject(PathTracingObject obj)
		{
			_pathTracingObjects.Remove(obj);
			_pathTracingDirty = true;
		}
		#endregion

		[Header("Rendering Parameters")]
		[SerializeField] private ComputeShader _pathTracingShader = null;
		[SerializeField] private Shader _progressiveShader = null;
		[SerializeField] private Texture _skyboxTexture = null;
		[SerializeField] private Texture[] _noiseTextures = null;

		[SerializeField] private Light _directionalLight = null;

		[Header("Interactions")]
		[SerializeField] private bool _liveRendering = true;
		[SerializeField] private UnityEngine.UI.RawImage _uiTarget = null;
		[SerializeField] private UnityEngine.UI.Text _frameText = null;

		[Header("Error (WIP)")]
		[SerializeField] private bool _computeError = false;
		[SerializeField] private Texture _referenceTexture = null;

		private Camera _camera = null;
		private RenderTexture _target = null;
		private RenderTexture _accumulated = null;

		private uint _currentSample = 0;
		private Material _progressiveMaterial = null;

		private List<float> _L2Norms = new List<float>();

		#region Compute Buffers
		private ComputeBuffer _planeBuffer = null;
		private ComputeBuffer _sphereBuffer = null;

		private ComputeBuffer _vertexBuffer = null;
		private ComputeBuffer _indexBuffer = null;
		private ComputeBuffer _meshBuffer = null;

		private ComputeBuffer _materialBuffer = null;

		ComputeBuffer _squaredDifferencesBuffer = null;
		#endregion

		#region Unity Callbacks
		private void Awake()
		{
			_camera = GetComponent<Camera>();
		}

		private void Start()
		{
			_currentSample = 0;
			SetupScene();
		}

		private void OnDisable()
		{
			Dispose();
		}

		private void Update()
		{
			if (transform.hasChanged || _directionalLight.transform.hasChanged)
			{
				ResetAccumulation();
				transform.hasChanged = false;
				_directionalLight.transform.hasChanged = false;
			}
		}

		private void ResetAccumulation()
        {
			_currentSample = 0;
		}

		private void OnRenderImage(RenderTexture source, RenderTexture destination)
		{
			if (_pathTracingShader != null && _liveRendering == true)
			{
				RenderNext();
				Graphics.Blit(_accumulated, destination);
			}
		}
		#endregion

		private void SetShaderParameters()
		{
			_pathTracingShader.SetTexture(0, "Result", _target);
			_pathTracingShader.SetTexture(0, "_SkyboxTexture", _skyboxTexture);
			_pathTracingShader.SetTexture(0, "_BlueNoiseTexture", _noiseTextures[_currentSample % _noiseTextures.Length]);

			if (_computeError && _squaredDifferencesBuffer != null && _referenceTexture != null)
			{
				_pathTracingShader.SetTexture(0, "_ReferenceImage", _referenceTexture);
				_pathTracingShader.SetBuffer(0, "_SquaredDifferences", _squaredDifferencesBuffer);
			}


			SetBuffer("_Planes", _planeBuffer);
			SetBuffer("_Spheres", _sphereBuffer);

			SetBuffer("_Vertices", _vertexBuffer);
			SetBuffer("_Indices", _indexBuffer);
			SetBuffer("_Meshes", _meshBuffer);

			SetBuffer("_Materials", _materialBuffer);

			Vector3 l = _directionalLight.transform.forward;
			_pathTracingShader.SetVector("_DirectionalLight", new Vector4(l.x, l.y, l.z, _directionalLight.intensity));

			_pathTracingShader.SetMatrix("_CameraToWorld", _camera.cameraToWorldMatrix);
			_pathTracingShader.SetMatrix("_CameraInverseProjection", _camera.projectionMatrix.inverse);

			_pathTracingShader.SetVector("_PixelOffset", new Vector2(Random.value, Random.value));
			_pathTracingShader.SetFloat("_Seed", Random.value);
			_pathTracingShader.SetVector("_SharedSeed", new Vector3(Random.value, Random.value, Random.value));
		}

		public void RenderNext()
		{
			// Validate our target RT, if the size corresponds to the viewport and so on.
			// If the RT is not valid, then we release it and create a new one.
			ValidateScreenRenderTexture(ref _target);
			ValidateScreenRenderTexture(ref _accumulated);

			SetShaderParameters();

			// Unity's defaut threadgroup size is 8x8x1, and we want one thread per pixel
			int threadGroupsX = Mathf.CeilToInt(Screen.width / 8.0f);
			int threadGroupsY = Mathf.CeilToInt(Screen.height / 8.0f);

			// Dispatch the computations
			_pathTracingShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);

			// Blit the result texture to the screen using a shader that help us to perform
			// progressive rendering
			if (_progressiveMaterial == null)
			{
				_progressiveMaterial = new Material(_progressiveShader);
			}

			_progressiveMaterial.SetFloat("_Sample", _currentSample);
			_currentSample++;

			Graphics.Blit(_target, _accumulated, _progressiveMaterial);

			if (_liveRendering == false)
			{
				if (_uiTarget != null)
				{
					_uiTarget.rectTransform.sizeDelta = new Vector2(_accumulated.width, _accumulated.height);
					_uiTarget.texture = _accumulated;
				}

				if (_frameText != null)
				{
					_frameText.text = "Frame: " + _currentSample;
				}
			}

			if (_computeError && _squaredDifferencesBuffer != null && _referenceTexture != null)
			{
				var sqDiffs = new float[_squaredDifferencesBuffer.count];
				_squaredDifferencesBuffer.GetData(sqDiffs);
				// Get L2 Norm
				float norm = sqDiffs.Sum();
				_L2Norms.Add(norm);
			}
		}

		private void ValidateScreenRenderTexture(ref RenderTexture texture)
		{
			if (texture == null || texture.width != Screen.width || texture.height != Screen.height)
			{
				// Release render texture if we already have one
				if (texture != null)
					texture.Release();
				// Get a render target
				texture = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
				texture.enableRandomWrite = true;
				texture.Create();
			}
		}

		private void SetupScene()
		{
			var planes = new List<PathTracingPlane>();
			var spheres = new List<PathTracingSphere>();

			var vertices = new List<Vector3>();
			var triangles = new List<uint>();
			var meshes = new List<PathTracingMesh>();

			var materials = new List<PathTracingMaterial>();

			foreach (var obj in _pathTracingObjects)
			{
				var material = obj.Material;
				uint materialIndex = (uint) materials.Count;
				materials.Add(material);

				switch (obj.Type)
				{
					case PathTracingObjectType.PLANE:
						var plane = new PathTracingPlane();
						plane.normal = obj.transform.up;
						plane.offset = -Vector3.Dot(obj.transform.position, plane.normal);
						plane.materialIndex = materialIndex;
						planes.Add(plane);
						break;
					case PathTracingObjectType.SPHERE:
						var sphere = new PathTracingSphere();
						sphere.position = obj.transform.position;
						sphere.radius = obj.transform.lossyScale.x * 0.5f;
						sphere.materialIndex = materialIndex;
						spheres.Add(sphere);
						break;
					case PathTracingObjectType.MESH:
						var mesh = new PathTracingMesh();
						Mesh unityMesh = obj.GetComponent<MeshFilter>().sharedMesh;

						uint firstVertex = (uint) vertices.Count;
						uint firstIndex = (uint) triangles.Count;

						var indices = unityMesh.GetIndices(0);
						var offsetIndices = new uint[indices.Length];
						for (uint i = 0; i < indices.Length; i++)
						{
							offsetIndices[i] = (uint) indices[i] + firstVertex;
						}

						vertices.AddRange(unityMesh.vertices);
						triangles.AddRange(offsetIndices);

						mesh.localToWorld = obj.transform.localToWorldMatrix;
						mesh.indicesOffset = firstIndex;
						mesh.indicesCount = (uint) indices.Length;
						mesh.materialIndex = materialIndex;

						meshes.Add(mesh);
						break;
				}
			}


			CreateComputeBuffer(ref _planeBuffer, planes, PathTracingPlane.Stride);
			CreateComputeBuffer(ref _sphereBuffer, spheres, PathTracingSphere.Stride);

			CreateComputeBuffer(ref _vertexBuffer, vertices, sizeof(float) * 3);
			CreateComputeBuffer(ref _indexBuffer, triangles, sizeof(uint));
			CreateComputeBuffer(ref _meshBuffer, meshes, PathTracingMesh.Stride);

			CreateComputeBuffer(ref _materialBuffer, materials, PathTracingMaterial.Stride);

			if (_computeError && _referenceTexture != null)
			{
				_squaredDifferencesBuffer = new ComputeBuffer(512 * 512, sizeof(float));
			}
		}

		private static void CreateComputeBuffer<T>(ref ComputeBuffer buffer, List<T> data, int stride) where T : struct
		{
			if (buffer != null)
			{
				// If no data or buffer doesn't match the given criteria, release it
				if (data.Count == 0 || buffer.count != data.Count || buffer.stride != stride)
				{
					ReleaseBuffer(buffer);
				}
			}


			if (data.Count > 0)
			{
				if (buffer == null)
				{
					buffer = new ComputeBuffer(data.Count, stride);
				}
				buffer.SetData(data);
			}
		}

		private static void ReleaseBuffer(ComputeBuffer buffer)
		{
			buffer?.Release();
			buffer = null;
		}

		private void SetBuffer(string name, ComputeBuffer buffer)
		{
			if (buffer != null)
			{
				_pathTracingShader.SetBuffer(0, name, buffer);
			}
		}

		private void Dispose()
		{
			ReleaseBuffer(_planeBuffer);
			ReleaseBuffer(_sphereBuffer);

			ReleaseBuffer(_vertexBuffer);
			ReleaseBuffer(_indexBuffer);
			ReleaseBuffer(_meshBuffer);

			ReleaseBuffer(_materialBuffer);

			ReleaseBuffer(_squaredDifferencesBuffer);
		}

		public void SaveToFile()
		{
			var lastRt = RenderTexture.active;
			RenderTexture.active = _accumulated;
			Texture2D tex = new Texture2D(_accumulated.width, _accumulated.height, TextureFormat.RGB24, false);
			tex.ReadPixels(new Rect(0, 0, _accumulated.width, _accumulated.height), 0, 0);
			RenderTexture.active = lastRt;

			byte[] bytes;
			bytes = tex.EncodeToPNG();

			string path = Application.dataPath + "/Output/PathTracedFrame_" + _currentSample + ".png";
			System.IO.File.WriteAllBytes(path, bytes);
			AssetDatabase.ImportAsset(path);
			Debug.Log("Saved to " + path);
		}

		public void ToggleImportanceSampling(bool enabled)
        {
			if (enabled)
            {
				_pathTracingShader.EnableKeyword("USE_IMPORTANCE_SAMPLING");
			}
			else
            {
				_pathTracingShader.DisableKeyword("USE_IMPORTANCE_SAMPLING");
			}
			ResetAccumulation();
        }

		public void ToggleBlueNoise(bool enabled)
		{
			if (enabled)
			{
				_pathTracingShader.EnableKeyword("USE_BLUE_NOISE");
			}
			else
			{
				_pathTracingShader.DisableKeyword("USE_BLUE_NOISE");
			}
			ResetAccumulation();
		}
	}

}