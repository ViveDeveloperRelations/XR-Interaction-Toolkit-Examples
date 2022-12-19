using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using Wave.Native;

namespace Wave.Essence.ScenePerception.Sample
{
	public class ScenePerceptionDemoManager : MonoBehaviour
	{
		public ScenePerceptionManager scenePerceptionManager = null;

		public List<MeshRenderer> RoomPanelMeshRenderers = new List<MeshRenderer>(5);

		public Material DemoRoomMaterialOpaque = null, DemoRoomMaterialTransparent = null, GeneratedMeshMaterialTranslucent = null;
		public Camera hmd = null;

		public GameObject leftController = null, rightController = null;

		private bool isSceneCMPStarted = false, hidePlanesAndAnchors = false;

		private WVR_ScenePerceptionTarget currentPerceptionTarget = WVR_ScenePerceptionTarget.WVR_ScenePerceptionTarget_2dPlane;

		private List<KeyValuePair<WVR_Uuid, WVR_ScenePlane>> currentScenePlaneList = new List<KeyValuePair<WVR_Uuid, WVR_ScenePlane>>();
		private List<KeyValuePair<WVR_Uuid, GameObject>> generatedPlaneGOList = new List<KeyValuePair<WVR_Uuid, GameObject>>();

		private WVR_SceneMesh[] currentSceneMeshes = new WVR_SceneMesh[0];
		private List<GameObject> generatedSceneMeshGOList = new List<GameObject>();

		private Dictionary<WVR_ScenePerceptionTarget, WVR_ScenePerceptionState> perceptionStateDictionary = new Dictionary<WVR_ScenePerceptionTarget, WVR_ScenePerceptionState>();

		public GameObject AnchorPrefab = null, AnchorDisplayPrefab = null;
		private GameObject AnchorDisplayRight = null;

		private RaycastHit leftControllerRaycastHitInfo = new RaycastHit(), rightControllerRaycastHitInfo = new RaycastHit();
		private Dictionary<ulong, GameObject> AnchorDictionary = new Dictionary<ulong, GameObject>();

		private const string LOG_TAG = "SceneMeshDemoManager";

		private void OnEnable()
		{
			//Check whether feature is supported on device or not
			if ((Interop.WVR_GetSupportedFeatures() & (ulong)WVR_SupportedFeature.WVR_SupportedFeature_ScenePerception) != 0)
			{
				WVR_Result result = scenePerceptionManager.StartScene();
				if (result == WVR_Result.WVR_Success)
				{
					isSceneCMPStarted = true;
					result = scenePerceptionManager.StartScenePerception(WVR_ScenePerceptionTarget.WVR_ScenePerceptionTarget_2dPlane);

					if (result == WVR_Result.WVR_Success)
					{
						ScenePerceptionGetState();
						needAnchorEnumeration = true;
					}
				}
			}
			else
			{
				Log.e(LOG_TAG, "Scene Perception is not available on the current device.");
			}
		}

		private void OnDisable()
		{
			if (isSceneCMPStarted)
			{
				scenePerceptionManager.StopScene();
				isSceneCMPStarted = false;
			}
		}

		private void OnApplicationPause(bool pause)
		{
			if (!pause)
			{
				needAnchorEnumeration = true; //Enumerate Anchors On Resume in case of a tracking map change
			}
		}

		private void Update()
		{ 
			if (isSceneCMPStarted && !hidePlanesAndAnchors)
			{
				//Log.d(LOG_TAG, "Update Plane and Anchors");
				//Handle Scene Perception
				ScenePerceptionGetState(); //Update state of scene perception every frame
				UpdateScenePerceptionMesh();

				//Handle Spatial Anchor
				UpdateAnchorDictionary();
			}


			if (WXRDevice.ButtonPress(WVR_DeviceType.WVR_DeviceType_Controller_Left, WVR_InputId.WVR_InputId_Alias1_X) && isSceneCMPStarted) //Destroy Anchors (Left)
			{
				HandleAnchorUpdateDestroy(leftControllerRaycastHitInfo);
			}

			if (WXRDevice.ButtonPress(WVR_DeviceType.WVR_DeviceType_Controller_Right, WVR_InputId.WVR_InputId_Alias1_A) && isSceneCMPStarted) //Create Anchors (Right)
			{
				HandleAnchorUpdateCreate(rightControllerRaycastHitInfo);
			}

			if (WXRDevice.ButtonPress(WVR_DeviceType.WVR_DeviceType_Controller_Left, WVR_InputId.WVR_InputId_Alias1_Y)) //Toggle Passthrough
			{
				ShowPassthroughUnderlay(!Interop.WVR_IsPassthroughOverlayVisible());
			}

			if (WXRDevice.ButtonPress(WVR_DeviceType.WVR_DeviceType_Controller_Right, WVR_InputId.WVR_InputId_Alias1_B)) //Toggle Planes and anchors
			{
				hidePlanesAndAnchors = !hidePlanesAndAnchors;
				Log.d(LOG_TAG, "hidePlanesAndAnchors: " + hidePlanesAndAnchors);
				if (hidePlanesAndAnchors)
				{
					DestroyGeneratedMeshes();
					ClearAnchors();
				}
			}
		}

		private void FixedUpdate()
		{
			if (AnchorDisplayRight == null)
			{
				AnchorDisplayRight = Instantiate(AnchorDisplayPrefab);
			}

			Physics.Raycast(leftController.transform.position, leftController.transform.forward, out leftControllerRaycastHitInfo);

			Physics.Raycast(rightController.transform.position, rightController.transform.forward, out rightControllerRaycastHitInfo);
			if (rightControllerRaycastHitInfo.collider != null && rightControllerRaycastHitInfo.collider.transform.GetComponent<AnchorPrefab>() == null) //Not hitting an anchor
			{
				AnchorDisplayRight.SetActive(true);
				AnchorDisplayRight.transform.position = rightControllerRaycastHitInfo.point;
				AnchorDisplayRight.transform.rotation = rightController.transform.rotation;
			}
			else
			{
				AnchorDisplayRight.SetActive(false);
			}
		}

		private void HandleAnchorUpdateDestroy(RaycastHit raycastHit)
		{
			WVR_Result result;

			if (raycastHit.collider != null)
			{
				if (raycastHit.collider.transform.GetComponent<AnchorPrefab>() != null) //Collider hit is an anchor (Destroy)
				{
					ulong targetAnchorHandle = raycastHit.collider.transform.GetComponent<AnchorPrefab>().anchorHandle;

					result = scenePerceptionManager.DestroySpatialAnchor(targetAnchorHandle);
					if (result == WVR_Result.WVR_Success)
					{
						Destroy(AnchorDictionary[targetAnchorHandle]);
						AnchorDictionary.Remove(targetAnchorHandle);

						needAnchorEnumeration = true;

						UpdateAnchorDictionary();
					}
				}
			}
		}

		private void HandleAnchorUpdateCreate(RaycastHit raycastHit)
		{
			WVR_Result result;

			if (raycastHit.collider != null)
			{
				if (raycastHit.collider.transform.GetComponent<AnchorPrefab>() == null) //Collider hit is not an anchor (Create)
				{
					Vector3 anchorWorldPositionUnity = raycastHit.point;

					string anchorNameString = "SpatialAnchor_" + (AnchorDictionary.Count + 1);
					char[] anchorNameArray = anchorNameString.ToCharArray();

					ulong newAnchorHandle = 0;
					result = scenePerceptionManager.CreateSpatialAnchor(anchorNameArray, anchorWorldPositionUnity, rightController.transform.rotation, ScenePerceptionManager.GetCurrentPoseOriginModel(), out newAnchorHandle, true);
					if (result == WVR_Result.WVR_Success)
					{
						needAnchorEnumeration = true;

						UpdateAnchorDictionary();
					}
				}
			}
		}

		ulong[] anchorHandles = null;
		bool needAnchorEnumeration = false;
		private void UpdateAnchorDictionary()
		{
			WVR_Result result;

			if (anchorHandles == null || needAnchorEnumeration)
			{
				result = scenePerceptionManager.GetSpatialAnchors(out anchorHandles);
				needAnchorEnumeration = false;
			}

			if (anchorHandles != null)
			{
				foreach (ulong anchorHandle in anchorHandles)
				{
					WVR_SpatialAnchorState currentAnchorState = default(WVR_SpatialAnchorState);
					result = scenePerceptionManager.GetSpatialAnchorState(anchorHandle, ScenePerceptionManager.GetCurrentPoseOriginModel(), out currentAnchorState);
					if (result == WVR_Result.WVR_Success)
					{
						Log.d(LOG_TAG, "Anchor Tracking State: " + currentAnchorState.trackingState.ToString());
						switch(currentAnchorState.trackingState)
						{
							case WVR_SpatialAnchorTrackingState.WVR_SpatialAnchorTrackingState_Tracking:
								{
									Vector3 currentAnchorPosition = Vector3.zero;
									Quaternion currentAnchorRotation = Quaternion.identity;

									if (!AnchorDictionary.ContainsKey(anchorHandle)) //Create Anchor Object
									{
										GameObject newAnchorGameObject = Instantiate(AnchorPrefab);

										AnchorPrefab newAnchorPrefabInstance = newAnchorGameObject.GetComponent<AnchorPrefab>();
										newAnchorPrefabInstance.anchorHandle = anchorHandle;
										newAnchorPrefabInstance.currentAnchorState = currentAnchorState;

										scenePerceptionManager.ApplyTrackingOriginCorrectionToAnchorPose(currentAnchorState, out currentAnchorPosition, out currentAnchorRotation);

										newAnchorGameObject.transform.position = currentAnchorPosition;
										newAnchorGameObject.transform.rotation = currentAnchorRotation;

										AnchorDictionary.Add(anchorHandle, newAnchorGameObject);
									}
									else //Anchor is already in dictionary
									{
										//Check anchor pose
										GameObject currentAnchorObject = AnchorDictionary[anchorHandle];
										AnchorPrefab currentAnchorPrefabInstance = currentAnchorObject.GetComponent<AnchorPrefab>();

										if (!ScenePerceptionManager.AnchorStatePoseEqual(currentAnchorPrefabInstance.currentAnchorState, currentAnchorState)) //Pose different, update
										{
											scenePerceptionManager.ApplyTrackingOriginCorrectionToAnchorPose(currentAnchorState, out currentAnchorPosition, out currentAnchorRotation);

											currentAnchorObject.transform.position = currentAnchorPosition;
											currentAnchorObject.transform.rotation = currentAnchorRotation;

											currentAnchorPrefabInstance.currentAnchorState = currentAnchorState;
										}

									}

									break;
								}
							case WVR_SpatialAnchorTrackingState.WVR_SpatialAnchorTrackingState_Paused:
							case WVR_SpatialAnchorTrackingState.WVR_SpatialAnchorTrackingState_Stopped:
							default:
								{
									//Remove from dictionary if exists
									if (AnchorDictionary.ContainsKey(anchorHandle))
									{
										Destroy(AnchorDictionary[anchorHandle]); //Destroy Anchor GO
										AnchorDictionary.Remove(anchorHandle);
									}
									break;
								}
						}
					}
				}
			}
		}

		public void UpdateScenePerceptionMesh()
		{
			WVR_Result result;

			if (perceptionStateDictionary[currentPerceptionTarget] == WVR_ScenePerceptionState.WVR_ScenePerceptionState_Completed)
			{
				switch(currentPerceptionTarget)
				{
					case WVR_ScenePerceptionTarget.WVR_ScenePerceptionTarget_2dPlane:
						{
							WVR_ScenePlane[] latestScenePlanes;
							result = scenePerceptionManager.GetScenePlanes(ScenePerceptionManager.GetCurrentPoseOriginModel(), out latestScenePlanes);

							if (result == WVR_Result.WVR_Success)
							{
								//Handle scene plane update
								//1. New planes
								//2. Existing but modified planes
								// - Changed pose -> modify transform
								// - Changed extend -> regenerate mesh
								//3. Remove any planes that are no longer existing

								//Check if existing plane still exsits
								List<int> planeIndexToRemove = new List<int>();
								for (int i=0; i< currentScenePlaneList.Count; i++)
								{
									bool planeExists = false;
									foreach (WVR_ScenePlane plane in latestScenePlanes)
									{
										if (ScenePerceptionManager.IsUUIDEqual(currentScenePlaneList[i].Key, plane.uuid)) //plane still exists
										{
											planeExists = true;
											break;
										}
									}

									if (!planeExists)
									{
										planeIndexToRemove.Add(i);
									}
								}

								foreach(int index in planeIndexToRemove) //Remove all planes that no longer exists
								{
									Log.d(LOG_TAG, "Remove deleted planes");
									currentScenePlaneList.RemoveAt(index);
									Destroy(generatedPlaneGOList[index].Value);
									generatedPlaneGOList.RemoveAt(index);
								}

								foreach (WVR_ScenePlane latestPlane in latestScenePlanes)
								{
									bool isNewPlane = true;
									KeyValuePair<WVR_Uuid, WVR_ScenePlane> latestUuidPlanePair = new KeyValuePair<WVR_Uuid, WVR_ScenePlane>(latestPlane.uuid, latestPlane);
									for (int i = 0; i < currentScenePlaneList.Count; i++)
									{
										KeyValuePair<WVR_Uuid, WVR_ScenePlane> currentUuidPlanePair = currentScenePlaneList[i];
										if (ScenePerceptionManager.IsUUIDEqual(currentUuidPlanePair.Key, latestPlane.uuid)) //Plane exists
										{
											//Log.d(LOG_TAG, "Plane Exists");
											if (!ScenePerceptionManager.ScenePlaneExtent2DEqual(latestPlane, currentUuidPlanePair.Value)) //Changed extend -> regenerate mesh
											{
												Log.d(LOG_TAG, "Plane Extent Changed");
												for (int j=0; j<generatedPlaneGOList.Count; j++)
												{
													KeyValuePair<WVR_Uuid, GameObject> currentUuidGOPair = generatedPlaneGOList[j];

													if (ScenePerceptionManager.IsUUIDEqual(latestPlane.uuid, currentUuidGOPair.Key)) //Entry to be updated
													{
														Destroy(currentUuidGOPair.Value); //Remove outdated GO
														generatedPlaneGOList[j] = new KeyValuePair<WVR_Uuid, GameObject>(latestPlane.uuid, scenePerceptionManager.GenerateScenePlaneMesh(latestPlane, GeneratedMeshMaterialTranslucent, true));
													}
												}

												currentScenePlaneList[i] = latestUuidPlanePair;
											}
											else if (!ScenePerceptionManager.ScenePlanePoseEqual(latestPlane, currentUuidPlanePair.Value)) //Changed pose -> modify transform
											{
												Log.d(LOG_TAG, "Plane Pose Changed");
												for (int j = 0; j < generatedPlaneGOList.Count; j++)
												{
													KeyValuePair<WVR_Uuid, GameObject> currentUuidGOPair = generatedPlaneGOList[j];

													if (ScenePerceptionManager.IsUUIDEqual(latestPlane.uuid, currentUuidGOPair.Key)) //Entry to be updated
													{
														Vector3 planePositionUnity = Vector3.zero;
														Quaternion planeRotationUnity = Quaternion.identity;

														scenePerceptionManager.ApplyTrackingOriginCorrectionToPlanePose(latestPlane, out planePositionUnity, out planeRotationUnity);

														generatedPlaneGOList[j].Value.transform.position = planePositionUnity;
														generatedPlaneGOList[j].Value.transform.rotation = planeRotationUnity;
													}
												}

												currentScenePlaneList[i] = latestUuidPlanePair;
											}

											isNewPlane = false;
											break;
										}
									}

									if (isNewPlane) // New Plane
									{
										Log.d(LOG_TAG, "Add new plane");
										currentScenePlaneList.Add(latestUuidPlanePair);
										generatedPlaneGOList.Add(new KeyValuePair<WVR_Uuid, GameObject>(latestPlane.uuid, scenePerceptionManager.GenerateScenePlaneMesh(latestPlane, GeneratedMeshMaterialTranslucent, true)));
									}
								}
							}

							break;
						}

					case WVR_ScenePerceptionTarget.WVR_ScenePerceptionTarget_3dObject:
					case WVR_ScenePerceptionTarget.WVR_ScenePerceptionTarget_VisualMesh:
					case WVR_ScenePerceptionTarget.WVR_ScenePerceptionTarget_ColliderMesh:
					default:
						break;
				}
			}
			else
			{
				Log.e(LOG_TAG, "OnClickGenerateMesh: Perception not complete, cannot generate mesh.");
			}
		}

		private void DestroyGeneratedMeshes()
		{
			if (currentScenePlaneList.Count > 0)
			{
				currentScenePlaneList.Clear();
			}

			if (generatedPlaneGOList.Count > 0)
			{
				foreach (KeyValuePair<WVR_Uuid, GameObject> uuidGOPair in generatedPlaneGOList)
				{
					MeshFilter generatedPlaneMeshFilter = uuidGOPair.Value.GetComponent<MeshFilter>();
					Destroy(generatedPlaneMeshFilter.mesh);
					Destroy(uuidGOPair.Value);
				}
				generatedPlaneGOList.Clear();
			}

			if (generatedSceneMeshGOList.Count > 0)
			{
				foreach (GameObject generatedSceneMeshGO in generatedSceneMeshGOList)
				{
					MeshFilter generatedSceneMeshFilter = generatedSceneMeshGO.GetComponent<MeshFilter>();
					Destroy(generatedSceneMeshFilter.mesh);
					Destroy(generatedSceneMeshGO);
				}
				generatedSceneMeshGOList.Clear();
			}
		}

		public void ScenePerceptionGetState()
		{
			WVR_ScenePerceptionState latestPerceptionState = WVR_ScenePerceptionState.WVR_ScenePerceptionState_Empty;
			WVR_Result result = scenePerceptionManager.GetScenePerceptionState(currentPerceptionTarget, ref latestPerceptionState);
			if (result == WVR_Result.WVR_Success)
			{
				perceptionStateDictionary[currentPerceptionTarget] = latestPerceptionState; //Update perception state for the perception target
			}
		}

		private void ClearAnchors()
		{
			foreach (KeyValuePair<ulong, GameObject> anchorPair in AnchorDictionary)
			{
				Destroy(anchorPair.Value);
			}

			AnchorDictionary.Clear();
		}

		public void ShowPassthroughUnderlay(bool show)
		{
			if (show)
			{
				//Set Demo Room Material to transparent and clear skybox to transparent black
				foreach(MeshRenderer meshRender in RoomPanelMeshRenderers)
				{
					meshRender.material = DemoRoomMaterialTransparent;
				}

				hmd.clearFlags = CameraClearFlags.SolidColor;
				hmd.backgroundColor = new Color(0, 0, 0, 0);
			}
			else
			{
				foreach (MeshRenderer meshRender in RoomPanelMeshRenderers)
				{
					meshRender.material = DemoRoomMaterialOpaque;
				}

				hmd.clearFlags = CameraClearFlags.Skybox;
			}

			Interop.WVR_ShowPassthroughUnderlay(show);
		}
	}
}
