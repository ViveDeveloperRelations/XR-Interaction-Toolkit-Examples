using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Wave.OpenXR;
using Random = UnityEngine.Random;

public class TestBuilder
    {
        [MenuItem("TestBuilder/TestBuild")]
        public static void Build()
        {
			Debug.Log("Start build");
            var buildNumberFaked = Convert.ToInt32(Random.Range(1f, 1000f));
            try
            {
                //other axes potentially - unity version, scripting backend, .net api compatibility, rendering pipeline, old/new input, xr input(xri with xr rig and xr origin), other packages, C++ compiler config, release/debug, tbd
                var buildConfig = new BuildConfig()
                {
                    BuildOptions = BuildOptions.Development, // | BuildOptions.AutoRunPlayer,
                    BuildTargetGroup = BuildTargetGroup.Android,
                    Scenes = BuildConfig.ScenesInApp(),
                    BundleVersionCode = buildNumberFaked, // PlayerSettings.Android.bundleVersionCode,
                    AppName = "XRI xr origin test "+buildNumberFaked+Application.unityVersion,
                    BundleIdentifier = "com.defaultCompany.xrorigin" +buildNumberFaked+ Application.unityVersion.Replace(".","_"),
                };
                Builder.BuildAndroid(buildConfig);
            }
            catch (Exception e)
            {
                Debug.LogError("Error during build");
                Debug.LogException(e);
            }
            Debug.Log("End build");
            EditorApplication.Exit(0);
        }

        public class BuildConfig
        {
            public BuildOptions BuildOptions = BuildOptions.None;
            public BuildTargetGroup BuildTargetGroup = BuildTargetGroup.Android;
            public string AppName = $"VRTestApp{EscapedUnityVersion()}";
            public string[] Scenes;
            public string BundleIdentifier;
            public int BundleVersionCode;

            private static string EscapedUnityVersion()
            {
                return Application.unityVersion.Replace(".", "_");
            }
            
            public static string[] ScenesInApp()
            {
                if (Application.levelCount == 0)
                {
                    throw new InvalidOperationException("No levels set in player settings");
                }
                List<string> scenes = new List<string>();
                foreach(var scene in EditorBuildSettings.scenes)
                {
                    if(scene.enabled)
                        scenes.Add(scene.path);
                }

                return scenes.ToArray();
            }
            public bool IsValid()
            {
                return BuildTargetGroup != BuildTargetGroup.Unknown && 
                       !string.IsNullOrEmpty(AppName) &&
                       !string.IsNullOrEmpty(BundleIdentifier) && 
                       Scenes != null && Scenes.Count((s => !string.IsNullOrEmpty(s))) >= 1; //at least one valid scene
            }
        }
        public class Builder
        {
            private static string SafeWindowsFileName(string input)
            {
                char[] invalidFileNameChars = Path.GetInvalidFileNameChars();

                // Builds a string out of valid chars
                var validFilename = new string(input.Where(ch => !invalidFileNameChars.Contains(ch)).ToArray());
                return validFilename;
            }
            public static void BuildAndroid(BuildConfig Config)
            {
                if (Config == null || !Config.IsValid())
                {
                    throw new ArgumentException("Invalid build config");
                }
                PlayerSettings.SetApplicationIdentifier(Config.BuildTargetGroup,Config.BundleIdentifier);
                PlayerSettings.productName = Config.AppName;
                //Application.productName = AppName;
                PlayerSettings.Android.bundleVersionCode = Config.BundleVersionCode;
                
                var apkName =  $"{SafeWindowsFileName(Config.BundleIdentifier.Replace(".", "_"))}.apk";
                const string buildDirName = "Builds";
                if (!Directory.Exists(buildDirName))
                    Directory.CreateDirectory(buildDirName);
                BuildPipeline.BuildPlayer(new BuildPlayerOptions()
                {
                    target = BuildTarget.Android, scenes = Config.Scenes, locationPathName = $"{buildDirName}/{apkName}",options = Config.BuildOptions
                });
            }
        }
    }
