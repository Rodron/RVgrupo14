namespace PaperPlaneTools.AR {
	using OpenCvSharp;

	using UnityEngine;
	using System.Collections;
	using System.Runtime.InteropServices;
	using System;
	using System.Collections.Generic;
	using UnityEngine.UI;
    using UnityEngine.SceneManagement;

    public class MainScript: WebCamera {
        public GameObject player;

        [Serializable]
		public class MarkerObject
		{
            public int markerId;
			public GameObject markerPrefab;
		}

		public class MarkerOnScene
		{            
            public int bestMatchIndex = -1;
			public float destroyAt = -1f;
			public GameObject gameObject = null;
		}

		/// <summary>
		/// List of possible markers
		/// The list is set in Unity Inspector
		/// </summary>
		public List<MarkerObject> markers;

		/// <summary>
		/// The marker detector
		/// </summary>
		private MarkerDetector markerDetector;


		/// <summary>
		/// Objects on scene
		/// </summary>
		private Dictionary<int, List<MarkerOnScene>> gameObjects = new Dictionary<int, List<MarkerOnScene>>();

		void Start () {
			markerDetector = new MarkerDetector ();

			foreach (MarkerObject markerObject in markers) {
				gameObjects.Add(markerObject.markerId, new List<MarkerOnScene>());
			}
		}


		protected override void Awake() {
			int cameraIndex = -1;
			for (int i = 0; i < WebCamTexture.devices.Length; i++) {
				WebCamDevice webCamDevice = WebCamTexture.devices [i];
				if (webCamDevice.isFrontFacing == false) {
					cameraIndex = i;
					break;
				}
				if (cameraIndex < 0) {
					cameraIndex = i;
				}
			}

			if (cameraIndex >= 0) {
				DeviceName = WebCamTexture.devices [cameraIndex].name;
				//webCamDevice = WebCamTexture.devices [cameraIndex];
			}
		}

		//Cambios nuestros: temporizador de salir al parar de escanear aruco		
    	private IEnumerator Scan(int time){
        	yield return new WaitForSeconds(time);
        	Selector.flag_scan = false;
    	}
		//
        
        protected override bool ProcessTexture(WebCamTexture input, ref Texture2D output)
        {
            var texture = new Texture2D(input.width, input.height);
            
            texture.SetPixels(input.GetPixels());
            var img = Unity.TextureToMat(texture, Unity.TextureConversionParams.Default);
            ProcessFrame(img, img.Cols, img.Rows);
            output = Unity.MatToTexture(img, output);
            UnityEngine.Object.Destroy(texture);

            return true;
        }

        private void ProcessFrame (Mat mat, int width, int height) {
			List<int> markerIds = markerDetector.Detect (mat, width, height);

			int count = 0;
			foreach (MarkerObject markerObject in markers) {
				List<int> foundedMarkers = new List<int>();
				for (int i=0; i<markerIds.Count; i++) {
					if (markerIds[i] == markerObject.markerId) {
						foundedMarkers.Add(i);
						count++;
					}
				}

				ProcessMarkersWithSameId(markerObject, gameObjects[markerObject.markerId], foundedMarkers);
			}
		}

		private void ProcessMarkersWithSameId(MarkerObject markerObject, List<MarkerOnScene> gameObjects, List<int> foundedMarkers) {
			int index = 0;			

            index = gameObjects.Count - 1;

			while (index >= 0) {
                MarkerOnScene markerOnScene = gameObjects[index];
				markerOnScene.bestMatchIndex = -1;
					if (markerOnScene.destroyAt > 0 && markerOnScene.destroyAt < Time.fixedTime)
					{
						//if(markerOnScene.gameObject != null){
							markerOnScene.gameObject.transform.parent = GameObject.FindGameObjectWithTag("sonidospadre").GetComponent<Transform>();
							StartCoroutine("Scan", 2);
						//}
					}
					else
					{
						StopCoroutine("Scan");
						Selector.flag_scan = true;
						markerOnScene.gameObject.GetComponentInChildren<AudioControl>().menu.SetActive(false);
						Component[] sonidos = GameObject.FindGameObjectWithTag("sonidospadre").GetComponentsInChildren<AudioControl>();
						foreach (AudioControl s in sonidos){
							s.menu.SetActive(false);
						}
						markerOnScene.gameObject.SetActive(true);
						markerOnScene.gameObject.transform.parent = player.GetComponent<Transform>();
					}
				--index;
                
			}
			
			index = foundedMarkers.Count - 1;

			// Match markers with existing gameObjects
			while (index >= 0) {
				int markerIndex = foundedMarkers[index];
				Matrix4x4 transforMatrix = markerDetector.TransfromMatrixForIndex(markerIndex);
				Vector3 position = MatrixHelper.GetPosition(transforMatrix);

				float minDistance = float.MaxValue;
				int bestMatch = -1;
				for (int i=0; i<gameObjects.Count; i++) {
					MarkerOnScene markerOnScene = gameObjects [i];
					if (markerOnScene.bestMatchIndex >= 0) {
						continue;
					}
					float distance = Vector3.Distance(markerOnScene.gameObject.transform.position, position);
					if (distance<minDistance) {
						bestMatch = i;
					}
				}

				if (bestMatch >=0) {
					gameObjects[bestMatch].bestMatchIndex = markerIndex;
					foundedMarkers.RemoveAt(index);
				} 
				--index;
			}


			//Destroy excessive objects
            
			index = gameObjects.Count - 1;
			while (index >= 0) {
				MarkerOnScene markerOnScene = gameObjects[index];
				if (markerOnScene.bestMatchIndex < 0) {
					if (markerOnScene.destroyAt < 0) {
						markerOnScene.destroyAt = Time.fixedTime + 0.2f;
					}
				} else {
					markerOnScene.destroyAt = -1f;
					int markerIndex = markerOnScene.bestMatchIndex;
					Matrix4x4 transforMatrix = markerDetector.TransfromMatrixForIndex(markerIndex);
					PositionObject(markerOnScene.gameObject, transforMatrix);
				}
				index--;
			}

			//Create objects for markers not matched with any game object
			foreach (int markerIndex in foundedMarkers) {
                GameObject gameObject = Instantiate(markerObject.markerPrefab);                
                //gameObject.transform.SetParent(player.GetComponent<Transform>());

				MarkerOnScene markerOnScene = new MarkerOnScene() {
					gameObject = gameObject
				};
				gameObjects.Add(markerOnScene);

				Matrix4x4 transforMatrix = markerDetector.TransfromMatrixForIndex(markerIndex);
				PositionObject(markerOnScene.gameObject, transforMatrix);
			}
		}

		private void PositionObject(GameObject gameObject, Matrix4x4 transformMatrix) {
			Matrix4x4 matrixY = Matrix4x4.TRS (Vector3.zero, Quaternion.identity, new Vector3 (1, -1, 1));
			Matrix4x4 matrixZ = Matrix4x4.TRS (Vector3.zero, Quaternion.identity, new Vector3 (1, 1, -1));
			Matrix4x4 matrix = matrixY * transformMatrix * matrixZ;

			gameObject.transform.localPosition = MatrixHelper.GetPosition (matrix);
			gameObject.transform.localRotation = MatrixHelper.GetQuaternion (matrix);
			gameObject.transform.localScale = MatrixHelper.GetScale (matrix);
		}
	}
}
