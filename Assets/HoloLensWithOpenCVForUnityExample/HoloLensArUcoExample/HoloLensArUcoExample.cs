﻿using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
using System.Threading;
using UnityEngine.UI;
using UnityEngine.EventSystems;

#if UNITY_5_3 || UNITY_5_3_OR_NEWER
using UnityEngine.SceneManagement;
#endif
using OpenCVForUnity;

namespace HoloLensWithOpenCVForUnityExample
{
    /// <summary>
    /// HoloLens ArUco example. (Example of marker based AR using the OpenCVForUnity on Hololens)
    /// https://github.com/opencv/opencv_contrib/blob/master/modules/aruco/samples/detect_markers.cpp
    /// </summary>
    [RequireComponent(typeof(WebCamTextureToMatHelper))]
    public class HoloLensArUcoExample : MonoBehaviour
    {
        [HeaderAttribute ("Preview")]

        /// <summary>
        /// The preview quad.
        /// </summary>
        public GameObject previewQuad;

        /// <summary>
        /// Determines if displays the camera preview.
        /// </summary>
        public bool displayCameraPreview;

        /// <summary>
        /// The toggle for switching the camera preview display state.
        /// </summary>
        public Toggle displayCameraPreviewToggle;


        [HeaderAttribute ("Detection")]

        /// <summary>
        /// Determines if enables the detection.
        /// </summary>
        public bool enableDetection = true;


        [HeaderAttribute ("AR")]

        /// <summary>
        /// Determines if applied the pose estimation.
        /// </summary>
        public bool applyEstimationPose = true;

        /// <summary>
        /// The downscale ratio.
        /// </summary>
        [TooltipAttribute("Factor by which the image will be scaled down before detection.")]
        public float downscaleRatio = 3;

        /// <summary>
        /// The dictionary identifier.
        /// </summary>
        public int dictionaryId = 10;

        /// <summary>
        /// The length of the marker.
        /// </summary>
        public float markerLength = 0.188f;

        /// <summary>
        /// The AR cube.
        /// </summary>
        public GameObject arCube;

        /// <summary>
        /// The AR game object.
        /// </summary>
        public GameObject arGameObject;

        /// <summary>
        /// The AR camera.
        /// </summary>
        public Camera arCamera;

        /// <summary>
        /// The cameraparam matrix.
        /// </summary>
        Mat camMatrix;

        /// <summary>
        /// The matrix that inverts the Y axis.
        /// </summary>
        Matrix4x4 invertYM;

        /// <summary>
        /// The matrix that inverts the Z axis.
        /// </summary>
        Matrix4x4 invertZM;

        /// <summary>
        /// The transformation matrix.
        /// </summary>
        Matrix4x4 transformationM;

        /// <summary>
        /// The transformation matrix for AR.
        /// </summary>
        Matrix4x4 ARM;

        /// <summary>
        /// The identifiers.
        /// </summary>
        Mat ids ;

        /// <summary>
        /// The corners.
        /// </summary>
        List<Mat> corners;

        /// <summary>
        /// The rejected.
        /// </summary>
        List<Mat> rejected;

        /// <summary>
        /// The rvecs.
        /// </summary>
        Mat rvecs;

        /// <summary>
        /// The tvecs.
        /// </summary>
        Mat tvecs;

        /// <summary>
        /// The rot mat.
        /// </summary>
        Mat rotMat;

        /// <summary>
        /// The detector parameters.
        /// </summary>
        DetectorParameters detectorParams;

        /// <summary>
        /// The dictionary.
        /// </summary>
        Dictionary dictionary;

        /// <summary>
        /// The webcam texture to mat helper.
        /// </summary>
        WebCamTextureToMatHelper webCamTextureToMatHelper;


        Mat grayMat;
        Mat rgbMat4preview;
        Texture2D texture;

        // Camera matrix value of Hololens camera 896x504 size. 
        // These values ​​are unique to my device, obtained from "Windows.Media.Devices.Core.CameraIntrinsics" class. (https://docs.microsoft.com/en-us/uwp/api/windows.media.devices.core.cameraintrinsics)
        // (can adjust the position of the AR hologram with the values ​​of cx and cy. see http://docs.opencv.org/2.4/modules/calib3d/doc/camera_calibration_and_3d_reconstruction.html)
        double fx = 1035.149;//focal length x.
        double fy = 1034.633;//focal length y.
        double cx = 404.9134;//principal point x.
        double cy = 236.2834;//principal point y.
        MatOfDouble distCoeffs;
        double distCoeffs1 = 0.2036923;//radial distortion coefficient k1.
        double distCoeffs2 = -0.2035773;//radial distortion coefficient k2.
        double distCoeffs3 = 0.0;//tangential distortion coefficient p1.
        double distCoeffs4 = 0.0;//tangential distortion coefficient p2.
        double distCoeffs5 = -0.2388065;//radial distortion coefficient k3.

        bool isDetecting = false;
        bool hasUpdatedARTransformMatrix = false;
        readonly static Queue<Action> ExecuteOnMainThread = new Queue<Action>();
        System.Object sync = new System.Object ();
        Mat rgbaMat4Thread;
        Mat downScaleRgbaMat;

        bool _isThreadRunning = false;
        bool isThreadRunning {
            get { lock (sync)
                return _isThreadRunning; }
            set { lock (sync)
                _isThreadRunning = value; }
        }

        // Use this for initialization
        void Start ()
        {
            if (downscaleRatio < 1)
                downscaleRatio = 1;

            displayCameraPreviewToggle.isOn = displayCameraPreview;

            webCamTextureToMatHelper = gameObject.GetComponent<WebCamTextureToMatHelper> ();
            webCamTextureToMatHelper.Initialize ();
        }

        /// <summary>
        /// Raises the web cam texture to mat helper initialized event.
        /// </summary>
        public void OnWebCamTextureToMatHelperInitialized ()
        {
            Debug.Log ("OnWebCamTextureToMatHelperInitialized");

            Mat webCamTextureMat = webCamTextureToMatHelper.GetMat ();

            Debug.Log ("Screen.width " + Screen.width + " Screen.height " + Screen.height + " Screen.orientation " + Screen.orientation);

            float width = Mathf.Round(webCamTextureMat.width() / downscaleRatio);
            float height = Mathf.Round(webCamTextureMat.height() / downscaleRatio);

            texture = new Texture2D ((int)width, (int)height, TextureFormat.RGB24, false);
            previewQuad.GetComponent<MeshRenderer>().material.mainTexture = texture;
            previewQuad.transform.localScale = new Vector3 (1, height/width, 1);
            previewQuad.SetActive (displayCameraPreview);

            double fx = this.fx;
            double fy = this.fy;
            double cx = this.cx / downscaleRatio;
            double cy = this.cy / downscaleRatio;

            camMatrix = new Mat (3, 3, CvType.CV_64FC1);
            camMatrix.put (0, 0, fx);
            camMatrix.put (0, 1, 0);
            camMatrix.put (0, 2, cx);
            camMatrix.put (1, 0, 0);
            camMatrix.put (1, 1, fy);
            camMatrix.put (1, 2, cy);
            camMatrix.put (2, 0, 0);
            camMatrix.put (2, 1, 0);
            camMatrix.put (2, 2, 1.0f);
            Debug.Log ("camMatrix " + camMatrix.dump ());

            distCoeffs = new MatOfDouble (distCoeffs1, distCoeffs2, distCoeffs3, distCoeffs4, distCoeffs5);
            Debug.Log ("distCoeffs " + distCoeffs.dump ());

            //Calibration camera
            Size imageSize = new Size (width, height);
            double apertureWidth = 0;
            double apertureHeight = 0;
            double[] fovx = new double[1];
            double[] fovy = new double[1];
            double[] focalLength = new double[1];
            Point principalPoint = new Point (0, 0);
            double[] aspectratio = new double[1];

            Calib3d.calibrationMatrixValues (camMatrix, imageSize, apertureWidth, apertureHeight, fovx, fovy, focalLength, principalPoint, aspectratio);

            Debug.Log ("imageSize " + imageSize.ToString ());
            Debug.Log ("apertureWidth " + apertureWidth);
            Debug.Log ("apertureHeight " + apertureHeight);
            Debug.Log ("fovx " + fovx [0]);
            Debug.Log ("fovy " + fovy [0]);
            Debug.Log ("focalLength " + focalLength [0]);
            Debug.Log ("principalPoint " + principalPoint.ToString ());
            Debug.Log ("aspectratio " + aspectratio [0]);


            grayMat = new Mat (webCamTextureMat.rows (), webCamTextureMat.cols (), CvType.CV_8UC3);
            ids = new Mat ();
            corners = new List<Mat> ();
            rejected = new List<Mat> ();
            rvecs = new Mat ();
            tvecs = new Mat ();
            rotMat = new Mat (3, 3, CvType.CV_64FC1);


            transformationM = new Matrix4x4 ();

            invertYM = Matrix4x4.TRS (Vector3.zero, Quaternion.identity, new Vector3 (1, -1, 1));
            Debug.Log ("invertYM " + invertYM.ToString ());

            invertZM = Matrix4x4.TRS (Vector3.zero, Quaternion.identity, new Vector3 (1, 1, -1));
            Debug.Log ("invertZM " + invertZM.ToString ());

            detectorParams = DetectorParameters.create ();
            dictionary = Aruco.getPredefinedDictionary (Aruco.DICT_6X6_250);


            //If WebCamera is frontFaceing,flip Mat.
            if (webCamTextureToMatHelper.GetWebCamDevice ().isFrontFacing) {
                webCamTextureToMatHelper.flipHorizontal = true;
            }

            rgbaMat4Thread = new Mat ();
            downScaleRgbaMat = new Mat ();
            rgbMat4preview = new Mat ();
        }

        /// <summary>
        /// Raises the web cam texture to mat helper disposed event.
        /// </summary>
        public void OnWebCamTextureToMatHelperDisposed ()
        {
            Debug.Log ("OnWebCamTextureToMatHelperDisposed");

            StopThread ();

            if (grayMat != null)
                grayMat.Dispose ();
            if (ids != null)
                ids.Dispose ();
            foreach (var item in corners) {
                item.Dispose ();
            }
            corners.Clear ();
            foreach (var item in rejected) {
                item.Dispose ();
            }
            rejected.Clear ();
            if (rvecs != null)
                rvecs.Dispose ();
            if (tvecs != null)
                tvecs.Dispose ();
            if (rotMat != null)
                rotMat.Dispose ();

            if (rgbaMat4Thread != null)
                rgbaMat4Thread.Dispose ();

            if (rgbMat4preview != null)
                rgbMat4preview.Dispose ();
        }

        /// <summary>
        /// Raises the web cam texture to mat helper error occurred event.
        /// </summary>
        /// <param name="errorCode">Error code.</param>
        public void OnWebCamTextureToMatHelperErrorOccurred(WebCamTextureToMatHelper.ErrorCode errorCode){
            Debug.Log ("OnWebCamTextureToMatHelperErrorOccurred " + errorCode);
        }

        // Update is called once per frame
        void Update ()
        {
            lock (sync) {
                while (ExecuteOnMainThread.Count > 0) {
                    ExecuteOnMainThread.Dequeue ().Invoke ();
                }
            }

            if (webCamTextureToMatHelper.IsPlaying () && webCamTextureToMatHelper.DidUpdateThisFrame ()) {
                
                if (enableDetection && !isDetecting ) {
                    isDetecting = true;

                    rgbaMat4Thread = webCamTextureToMatHelper.GetMat ();

                    StartThread (ThreadWorker);
                }
            }
        }

        private void StartThread(Action action)
        {
            #if UNITY_METRO && NETFX_CORE
            System.Threading.Tasks.Task.Run(() => action());
            #elif UNITY_METRO
            action.BeginInvoke(ar => action.EndInvoke(ar), null);
            #else
            ThreadPool.QueueUserWorkItem (_ => action());
            #endif
        }

        private void StopThread ()
        {
            if (!isThreadRunning)
                return;

            while (isThreadRunning) {
                //Wait threading stop
            } 
        }

        private void ThreadWorker()
        {
            isThreadRunning = true;

            DetectARUcoMarker ();

            lock (sync) {
                if (ExecuteOnMainThread.Count == 0) {
                    ExecuteOnMainThread.Enqueue (() => {
                        OnDetectionDone ();
                    });
                }
            }

            isThreadRunning = false;
        }

        private void DetectARUcoMarker()
        {
            if (downscaleRatio == 1) {
                downScaleRgbaMat = rgbaMat4Thread;
            } else {
                Imgproc.resize (rgbaMat4Thread, downScaleRgbaMat, new Size (), 1.0 / downscaleRatio, 1.0 / downscaleRatio, Imgproc.INTER_LINEAR);
            }
            Imgproc.cvtColor (downScaleRgbaMat, grayMat, Imgproc.COLOR_RGBA2GRAY);

            // Detect markers and estimate Pose
            Aruco.detectMarkers (grayMat, dictionary, corners, ids, detectorParams, rejected);

            if (applyEstimationPose && ids.total () > 0){
                Aruco.estimatePoseSingleMarkers (corners, markerLength, camMatrix, distCoeffs, rvecs, tvecs);

                for (int i = 0; i < ids.total (); i++) {

                    //This example can display ARObject on only first detected marker.
                    if (i == 0) {

                        // Position
                        double[] tvec = tvecs.get (i, 0);

                        // Rotation
                        double[] rv = rvecs.get (i, 0);
                        Mat rvec = new Mat (3, 1, CvType.CV_64FC1);
                        rvec.put (0, 0, rv [0]);
                        rvec.put (1, 0, rv [1]);
                        rvec.put (2, 0, rv [2]);
                        Calib3d.Rodrigues (rvec, rotMat);

                        transformationM.SetRow (0, new Vector4 ((float)rotMat.get (0, 0) [0], (float)rotMat.get (0, 1) [0], (float)rotMat.get (0, 2) [0], (float)tvec [0]));
                        transformationM.SetRow (1, new Vector4 ((float)rotMat.get (1, 0) [0], (float)rotMat.get (1, 1) [0], (float)rotMat.get (1, 2) [0], (float)tvec [1]));
                        transformationM.SetRow (2, new Vector4 ((float)rotMat.get (2, 0) [0], (float)rotMat.get (2, 1) [0], (float)rotMat.get (2, 2) [0], (float)(tvec [2] / downscaleRatio)));

                        transformationM.SetRow (3, new Vector4 (0, 0, 0, 1));

                        // Right-handed coordinates system (OpenCV) to left-handed one (Unity)
                        ARM = invertYM * transformationM;

                        // Apply Z axis inverted matrix.
                        ARM = ARM * invertZM;

                        hasUpdatedARTransformMatrix = true;

                        break;
                    }
                }
            }
        }

        private void OnDetectionDone()
        {
            if (displayCameraPreview) {
                Imgproc.cvtColor (downScaleRgbaMat, rgbMat4preview, Imgproc.COLOR_RGBA2RGB);

                if (ids.total () > 0) {
                    Aruco.drawDetectedMarkers (rgbMat4preview, corners, ids, new Scalar (255, 0, 0));

                    for (int i = 0; i < ids.total (); i++) {
                        Aruco.drawAxis (rgbMat4preview, camMatrix, distCoeffs, rvecs, tvecs, markerLength * 0.5f);
                    }
                }

                OpenCVForUnity.Utils.fastMatToTexture2D (rgbMat4preview, texture);
            }

            if (applyEstimationPose) {
                if (hasUpdatedARTransformMatrix) {
                    hasUpdatedARTransformMatrix = false;

                    // Apply camera transform matrix.
                    ARM = arCamera.transform.localToWorldMatrix * ARM;
                    ARUtils.SetTransformFromMatrix (arGameObject.transform, ref ARM);
                }
            }

            isDetecting = false;
        }

        /// <summary>
        /// Raises the disable event.
        /// </summary>
        void OnDisable ()
        {
            webCamTextureToMatHelper.Dispose ();
        }

        /// <summary>
        /// Raises the back button click event.
        /// </summary>
        public void OnBackButtonClick ()
        {
            #if UNITY_5_3 || UNITY_5_3_OR_NEWER
            SceneManager.LoadScene ("HoloLensWithOpenCVForUnityExample");
            #else
            Application.LoadLevel ("HoloLensWithOpenCVForUnityExample");
            #endif
        }

        /// <summary>
        /// Raises the play button click event.
        /// </summary>
        public void OnPlayButtonClick ()
        {
            webCamTextureToMatHelper.Play ();
        }

        /// <summary>
        /// Raises the pause button click event.
        /// </summary>
        public void OnPauseButtonClick ()
        {
            webCamTextureToMatHelper.Pause ();
        }

        /// <summary>
        /// Raises the stop button click event.
        /// </summary>
        public void OnStopButtonClick ()
        {
            webCamTextureToMatHelper.Stop ();
        }

        /// <summary>
        /// Raises the change camera button click event.
        /// </summary>
        public void OnChangeCameraButtonClick ()
        {
            webCamTextureToMatHelper.Initialize (null, webCamTextureToMatHelper.requestedWidth, webCamTextureToMatHelper.requestedHeight, !webCamTextureToMatHelper.requestedIsFrontFacing);
        }

        /// <summary>
        /// Raises the display camera preview toggle value changed event.
        /// </summary>
        public void OnDisplayCamreaPreviewToggleValueChanged ()
        {
            if (displayCameraPreviewToggle.isOn) {
                displayCameraPreview = true;
            } else {
                displayCameraPreview = false;
            }
            previewQuad.SetActive (displayCameraPreview);
        }

        /// <summary>
        /// Raises the tapped event.
        /// </summary>
        public void OnTapped ()
        {
            if (EventSystem.current.IsPointerOverGameObject ())
                return;

            if (applyEstimationPose) {
                applyEstimationPose = false;
                arCube.GetComponent<MeshRenderer> ().material.color = Color.gray;
            } else {
                applyEstimationPose = true;
                arCube.GetComponent<MeshRenderer> ().material.color = Color.red;
            }
        }
    }
}