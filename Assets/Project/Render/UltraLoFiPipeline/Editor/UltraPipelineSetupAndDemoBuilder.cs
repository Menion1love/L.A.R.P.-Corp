using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace UltraLoFiPipeline.Editor
{
    /// <summary>
    /// One-click pipeline installer and showcase demo scene builder for the Ultra Lo-Fi Realistic Render Pipeline.
    /// Uses a single unified UltraPipelineProfile asset for all settings.
    /// </summary>
    public static class UltraPipelineSetupAndDemoBuilder
    {
        private static string RootFolder => GetPipelineRootFolder();
        private static string ProfilesFolder => RootFolder + "/Profiles";
        private static string MaterialsFolder => RootFolder + "/Materials";
        private static string TexturesFolder => RootFolder + "/Textures";
        private static string MeshesFolder => RootFolder + "/Meshes";
        private static string MasterProfilePath => ProfilesFolder + "/UltraPipelineProfile.asset";

        private static string GetPipelineRootFolder()
        {
            string[] guids = AssetDatabase.FindAssets("UltraPipelineSetupAndDemoBuilder t:MonoScript");
            if (guids != null && guids.Length > 0)
            {
                string scriptPath = AssetDatabase.GUIDToAssetPath(guids[0]).Replace('\\', '/');
                int editorIdx = scriptPath.LastIndexOf("/Editor/", System.StringComparison.OrdinalIgnoreCase);
                if (editorIdx > 0)
                    return scriptPath.Substring(0, editorIdx);
            }
            return "Assets/UltraLoFiPipeline";
        }

        [MenuItem("Tools/Ultra Lo-Fi Pipeline/1. Setup & Activate Pipeline in Project", priority = 10)]
        public static UltraPipelineProfile SetupAndActivatePipeline()
        {
            EnsureFolders();

            var masterProfile = CreateOrUpdateMasterProfile(MasterProfilePath);
            ConfigureURPAssets(masterProfile);
            int fixedMats = UpgradeBuiltInAndPinkMaterialsToURP();
            AttachManagerToSceneCameras(masterProfile);

            Shader lightShader = Shader.Find("Hidden/UltraLoFiPipeline/UltraLightingEffects");
            Shader postShader = Shader.Find("Hidden/UltraLoFiPipeline/UltraPostProcessAndUpscale");
            if (lightShader == null || postShader == null)
            {
                Debug.LogError("[UltraLoFiPipeline] Missing pipeline shaders! Make sure you copied the entire 'Shaders' folder (UltraLightingEffects.shader, UltraPostProcessAndUpscale.shader, UltraHardwareRayTracing.raytrace, UltraRealisticLit.shader) into " + RootFolder + "/Shaders/");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            Debug.Log($"[UltraLoFiPipeline] Pipeline activated at '{RootFolder}' with single unified profile: {MasterProfilePath} (Auto-upgraded {fixedMats} Built-in/Pink materials to URP).");
            return masterProfile;
        }

        [MenuItem("Tools/Ultra Lo-Fi Pipeline/3. Fix Pink (Built-in Standard) Materials in Project", priority = 30)]
        public static void FixPinkMaterialsMenu()
        {
            int count = UpgradeBuiltInAndPinkMaterialsToURP();
            AssetDatabase.SaveAssets();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            Debug.Log($"[UltraLoFiPipeline] Upgraded {count} Built-in/Pink materials to URP.");
        }

        [MenuItem("Tools/Ultra Lo-Fi Pipeline/2. Build Ultra-Realistic Showcase Demo Scene", priority = 20)]
        public static void BuildShowcaseDemoScene()
        {
            UltraPipelineProfile activeProfile = SetupAndActivatePipeline();

            // Generate smooth 512x512 Trilinear procedural textures (no low-res 128x128 Point pixelation or dark rectangular frames!)
            Texture2D stoneAlbedo, stoneNormal, marbleAlbedo, marbleNormal;
            GenerateProceduralTextures(out stoneAlbedo, out stoneNormal, out marbleAlbedo, out marbleNormal);
            Mesh torusKnotMesh = GenerateOrLoadTorusKnotMesh(MeshesFolder + "/UltraTorusKnotSculpture.asset");
            Mesh highPolySphereMesh = GenerateOrLoadHighPolySphereMesh(MeshesFolder + "/UltraHighPolySphere.asset");

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Material skyMat = CreateOrUpdateSkyboxMaterial();
            RenderSettings.skybox = skyMat;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.46f, 0.58f, 0.82f);
            RenderSettings.ambientEquatorColor = new Color(0.34f, 0.31f, 0.29f);
            RenderSettings.ambientGroundColor = new Color(0.16f, 0.13f, 0.11f);
            RenderSettings.reflectionIntensity = 1.15f;
            RenderSettings.fog = false;

            Shader ultraLitShader = Shader.Find("UltraLoFiPipeline/UltraRealisticLit");
            if (ultraLitShader == null)
                ultraLitShader = Shader.Find("Universal Render Pipeline/Lit");

            Material matFloorObsidian = CreateUltraLitMaterial(
                "Mat_PolishedObsidianFloor", ultraLitShader,
                new Color(0.16f, 0.17f, 0.20f), metallic: 0.15f, smoothness: 0.88f,
                albedoMap: stoneAlbedo, normalMap: stoneNormal, normalScale: 0.35f, tiling: new Vector2(6f, 6f),
                clearCoatMask: 0.55f, clearCoatSmoothness: 0.94f);

            Material matStonePillar = CreateUltraLitMaterial(
                "Mat_WarmLimestonePillar", ultraLitShader,
                new Color(0.84f, 0.80f, 0.73f), metallic: 0.0f, smoothness: 0.48f,
                albedoMap: marbleAlbedo, normalMap: marbleNormal, normalScale: 0.45f, tiling: new Vector2(2f, 4f));

            // Smooth continuous architectural walls without any tile grid or dark rectangular borders
            Material matBounceWallRed = CreateUltraLitMaterial(
                "Mat_CrimsonBounceWall", ultraLitShader,
                new Color(0.86f, 0.18f, 0.14f), metallic: 0.0f, smoothness: 0.38f);

            Material matBounceWallTeal = CreateUltraLitMaterial(
                "Mat_CobaltTealBounceWall", ultraLitShader,
                new Color(0.12f, 0.55f, 0.78f), metallic: 0.0f, smoothness: 0.38f);

            // Subsurface Scattering & Translucent Hero Materials
            Material matJadeSSS = CreateUltraLitMaterial(
                "Mat_SSS_ImperialJade", ultraLitShader,
                new Color(0.16f, 0.72f, 0.44f), metallic: 0.0f, smoothness: 0.86f,
                normalMap: marbleNormal, normalScale: 0.20f, tiling: new Vector2(2f, 2f),
                sssStrength: 1.15f, sssColor: new Color(0.22f, 1.15f, 0.55f), sssWrap: 0.55f,
                translucencyStrength: 1.45f, translucencyPower: 3.8f, thickness: 0.25f,
                clearCoatMask: 0.5f, clearCoatSmoothness: 0.92f);

            Material matWaxAmberSSS = CreateUltraLitMaterial(
                "Mat_SSS_RoyalAmberWax", ultraLitShader,
                new Color(0.92f, 0.48f, 0.20f), metallic: 0.0f, smoothness: 0.78f,
                normalMap: marbleNormal, normalScale: 0.18f, tiling: new Vector2(2f, 2f),
                sssStrength: 1.25f, sssColor: new Color(1.25f, 0.35f, 0.10f), sssWrap: 0.60f,
                translucencyStrength: 1.55f, translucencyPower: 3.6f, thickness: 0.24f,
                clearCoatMask: 0.35f, clearCoatSmoothness: 0.86f);

            Material matRoseMarbleSSS = CreateUltraLitMaterial(
                "Mat_SSS_AlabasterRoseMarble", ultraLitShader,
                new Color(0.94f, 0.84f, 0.80f), metallic: 0.0f, smoothness: 0.84f,
                albedoMap: marbleAlbedo, normalMap: marbleNormal, normalScale: 0.25f, tiling: new Vector2(2f, 2f),
                sssStrength: 0.95f, sssColor: new Color(1.15f, 0.34f, 0.20f), sssWrap: 0.50f,
                translucencyStrength: 1.25f, translucencyPower: 4.0f, thickness: 0.28f,
                clearCoatMask: 0.6f, clearCoatSmoothness: 0.94f);

            // Calibrated PBR & Emissive Materials
            Material matGold = CreateUltraLitMaterial(
                "Mat_PBR_24KGold", ultraLitShader,
                new Color(1.0f, 0.78f, 0.34f), metallic: 1.0f, smoothness: 0.92f);

            Material matChrome = CreateUltraLitMaterial(
                "Mat_PBR_MirrorChrome", ultraLitShader,
                new Color(0.95f, 0.96f, 0.98f), metallic: 1.0f, smoothness: 0.98f);

            Material matCopper = CreateUltraLitMaterial(
                "Mat_PBR_AnodizedCopper", ultraLitShader,
                new Color(0.92f, 0.45f, 0.30f), metallic: 0.94f, smoothness: 0.86f,
                clearCoatMask: 0.45f, clearCoatSmoothness: 0.94f);

            Material matEmissiveAmber = CreateUltraLitMaterial(
                "Mat_Emissive_SolarAmber", ultraLitShader,
                new Color(1.0f, 0.75f, 0.35f), metallic: 0.0f, smoothness: 0.9f,
                emissionColor: new Color(4.2f, 2.5f, 0.75f));

            Material matEmissiveCyan = CreateUltraLitMaterial(
                "Mat_Emissive_PlasmaCyan", ultraLitShader,
                new Color(0.35f, 0.9f, 1.0f), metallic: 0.0f, smoothness: 0.9f,
                emissionColor: new Color(0.75f, 2.9f, 4.5f));

            // --------------------------------------------------------------
            // Construct Scene Hierarchy
            // --------------------------------------------------------------
            GameObject envRoot = new GameObject("Environment_Architecture");
            GameObject heroRoot = new GameObject("Showcase_SSS_And_PBR_Artifacts");
            GameObject lightsRoot = new GameObject("Lighting");

            // 1. Reflective Obsidian Courtyard Floor & Stepped Platform
            CreatePrimitive("Courtyard_Floor", PrimitiveType.Cube, envRoot.transform,
                new Vector3(0f, -0.25f, 6f), new Vector3(22f, 0.5f, 24f), Quaternion.identity, matFloorObsidian);

            CreatePrimitive("Center_Sanctuary_Dais", PrimitiveType.Cube, envRoot.transform,
                new Vector3(0f, 0.12f, 6.2f), new Vector3(8.5f, 0.24f, 6.5f), Quaternion.identity, matStonePillar);

            // 2. Smooth RTGI Color-Bounce Side Walls (Crimson Left, Cobalt Teal Right)
            CreatePrimitive("Wall_Left_CrimsonBounce", PrimitiveType.Cube, envRoot.transform,
                new Vector3(-7.5f, 3.5f, 6f), new Vector3(0.6f, 7.0f, 18f), Quaternion.identity, matBounceWallRed);

            CreatePrimitive("Wall_Right_CobaltBounce", PrimitiveType.Cube, envRoot.transform,
                new Vector3(7.5f, 3.5f, 6f), new Vector3(0.6f, 7.0f, 18f), Quaternion.identity, matBounceWallTeal);

            for (int i = -2; i <= 2; i++)
            {
                float x = i * 3.1f;
                CreatePrimitive($"BackColonnade_Pillar_{i}", PrimitiveType.Cube, envRoot.transform,
                    new Vector3(x, 3.5f, 14.5f), new Vector3(1.15f, 7.0f, 1.15f), Quaternion.identity, matStonePillar);
            }
            CreatePrimitive("BackArchitrave_TopBeam", PrimitiveType.Cube, envRoot.transform,
                new Vector3(0f, 6.8f, 14.5f), new Vector3(16f, 0.9f, 1.5f), Quaternion.identity, matStonePillar);

            // 3. Flanked Courtyard Pillars (Open Sky Ceiling for Clean, Unobstructed Sphere Reflections)
            float[] pillarZ = { 2.8f, 6.2f, 9.8f };
            for (int i = 0; i < pillarZ.Length; i++)
            {
                float z = pillarZ[i];
                CreatePillarAssembly($"Pillar_Left_{i}", envRoot.transform, new Vector3(-4.6f, 0f, z), matStonePillar);
                CreatePillarAssembly($"Pillar_Right_{i}", envRoot.transform, new Vector3(4.6f, 0f, z), matStonePillar);
            }

            // 4. Three Hero Subsurface Scattering (SSS) & Translucency Pedestals (Positioned close to spheres for rich reflections)
            CreatePedestal("Pedestal_Center_AmberSSS", heroRoot.transform, new Vector3(0f, 0.24f, 4.85f), matStonePillar);
            CreateMeshObject("Artifact_Center_AmberWax_SSS", torusKnotMesh, heroRoot.transform,
                new Vector3(0f, 2.20f, 4.85f), new Vector3(0.96f, 0.96f, 0.96f), Quaternion.Euler(15f, 30f, 0f), matWaxAmberSSS);

            CreatePedestal("Pedestal_Left_JadeSSS", heroRoot.transform, new Vector3(-2.30f, 0.24f, 5.45f), matStonePillar);
            CreateMeshObject("Artifact_Left_ImperialJade_SSS", torusKnotMesh, heroRoot.transform,
                new Vector3(-2.30f, 2.12f, 5.45f), new Vector3(0.85f, 0.85f, 0.85f), Quaternion.Euler(0f, -40f, 20f), matJadeSSS);

            CreatePedestal("Pedestal_Right_MarbleSSS", heroRoot.transform, new Vector3(2.30f, 0.24f, 5.45f), matStonePillar);
            CreateMeshObject("Artifact_Right_RoseMarble_SSS", torusKnotMesh, heroRoot.transform,
                new Vector3(2.30f, 2.12f, 5.45f), new Vector3(0.85f, 0.85f, 0.85f), Quaternion.Euler(25f, 55f, 0f), matRoseMarbleSSS);

            // 5. Foreground PBR & Hardware Ray-Traced Reflection Calibration Spheres (High-Poly Smooth Analytic Normals)
            Material[] pbrRowMats = { matGold, matChrome, matWaxAmberSSS, matJadeSSS, matCopper };
            string[] pbrNames = { "Sphere_24KGold", "Sphere_MirrorChrome", "Sphere_SubsurfaceWax", "Sphere_SubsurfaceJade", "Sphere_ClearcoatCopper" };
            for (int i = 0; i < pbrRowMats.Length; i++)
            {
                float x = (i - 2) * 1.32f;
                Vector3 spherePos = new Vector3(x, 0.72f, 3.25f);
                CreateMeshObject(pbrNames[i], highPolySphereMesh, heroRoot.transform,
                    spherePos, new Vector3(0.88f, 0.88f, 0.88f), Quaternion.identity, pbrRowMats[i]);

                CreatePrimitive($"Plinth_{pbrNames[i]}", PrimitiveType.Cylinder, heroRoot.transform,
                    new Vector3(x, 0.26f, 3.25f), new Vector3(0.92f, 0.06f, 0.92f), Quaternion.identity, matStonePillar);
            }

            // 6. Emissive Light Obelisks
            CreatePrimitive("Emissive_Obelisk_LeftAmber", PrimitiveType.Cube, heroRoot.transform,
                new Vector3(-6.85f, 2.2f, 4.6f), new Vector3(0.24f, 3.4f, 0.55f), Quaternion.identity, matEmissiveAmber);
            CreatePrimitive("Emissive_Obelisk_RightCyan", PrimitiveType.Cube, heroRoot.transform,
                new Vector3(6.85f, 2.2f, 4.6f), new Vector3(0.24f, 3.4f, 0.55f), Quaternion.identity, matEmissiveCyan);
            CreateMeshObject("Emissive_ArchBeacon_Center", highPolySphereMesh, heroRoot.transform,
                new Vector3(0f, 3.85f, 8.8f), new Vector3(0.65f, 0.65f, 0.65f), Quaternion.identity, matEmissiveAmber);

            // 7. Lighting Setup
            GameObject sunGO = new GameObject("Directional_Sun_MainLight");
            sunGO.transform.SetParent(lightsRoot.transform);
            sunGO.transform.rotation = Quaternion.Euler(34f, -158f, 0f);
            Light sunLight = sunGO.AddComponent<Light>();
            sunLight.type = LightType.Directional;
            sunLight.color = new Color(1.0f, 0.93f, 0.80f);
            sunLight.intensity = 3.2f;
            sunLight.shadows = LightShadows.Soft;
            sunLight.shadowBias = 0.02f;
            sunLight.shadowNormalBias = 0.3f;
            var sunAddData = sunGO.AddComponent<UniversalAdditionalLightData>();
            sunAddData.usePipelineSettings = true;

            CreatePointLight("Light_BackTranslucency_CenterAmber", lightsRoot.transform,
                new Vector3(0f, 2.4f, 6.45f), new Color(1.0f, 0.65f, 0.30f), intensity: 2.6f, range: 5.5f, shadows: false);

            CreatePointLight("Light_BackTranslucency_LeftJade", lightsRoot.transform,
                new Vector3(-2.6f, 2.3f, 7.15f), new Color(0.85f, 1.0f, 0.70f), intensity: 2.4f, range: 5.5f, shadows: false);

            CreatePointLight("Light_BackTranslucency_RightMarble", lightsRoot.transform,
                new Vector3(2.6f, 2.3f, 7.15f), new Color(0.65f, 0.88f, 1.0f), intensity: 2.4f, range: 5.5f, shadows: false);

            CreatePointLight("Light_AlcoveBounce_LeftAmber", lightsRoot.transform,
                new Vector3(-5.8f, 2.4f, 4.8f), new Color(1.0f, 0.52f, 0.25f), intensity: 4.5f, range: 8.5f, shadows: false);

            CreatePointLight("Light_AlcoveBounce_RightCyan", lightsRoot.transform,
                new Vector3(5.8f, 2.4f, 4.8f), new Color(0.25f, 0.80f, 1.0f), intensity: 4.5f, range: 8.5f, shadows: false);

            // 8. Main Camera with UltraLowResPipelineManager pointing to the single UltraPipelineProfile
            GameObject camGO = new GameObject("Main Camera");
            camGO.tag = "MainCamera";
            camGO.transform.position = new Vector3(0f, 2.15f, -1.35f);
            camGO.transform.rotation = Quaternion.Euler(7.5f, 0f, 0f);

            Camera cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.15f;
            cam.farClipPlane = 120f;
            cam.allowHDR = true;
            cam.allowMSAA = false;

            var camData = camGO.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = false;
            camData.antialiasing = AntialiasingMode.None;
            camData.renderShadows = true;
            camData.requiresDepthOption = CameraOverrideOption.On;
            camData.requiresColorOption = CameraOverrideOption.On;

            camGO.AddComponent<AudioListener>();

            var pipelineManager = camGO.AddComponent<UltraLowResPipelineManager>();
            pipelineManager.profile = activeProfile;
            pipelineManager.SyncPipelineSettings();

            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.AlignViewToObject(camGO.transform);
                SceneView.lastActiveSceneView.Repaint();
            }

            if (!Directory.Exists("Assets/Scenes"))
                AssetDatabase.CreateFolder("Assets", "Scenes");

            string sampleScenePath = "Assets/Scenes/SampleScene.unity";
            EditorSceneManager.SaveScene(scene, sampleScenePath);

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(sampleScenePath, true)
            };

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            DisableGameViewGizmos();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            Debug.Log("[UltraLoFiPipeline] Showcase Demo Scene built and saved to Assets/Scenes/SampleScene.unity.");
        }

        private static void DisableGameViewGizmos()
        {
            var gameViewType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
            if (gameViewType == null) return;
            var gizmosField = gameViewType.GetField("m_Gizmos", BindingFlags.Instance | BindingFlags.NonPublic);
            if (gizmosField == null) return;
            var windows = Resources.FindObjectsOfTypeAll(gameViewType);
            foreach (var win in windows)
            {
                gizmosField.SetValue(win, false);
                if (win is EditorWindow ew) ew.Repaint();
            }
        }

        private static void EnsureFolders()
        {
            string root = RootFolder;
            if (!AssetDatabase.IsValidFolder(root))
                CreateFolderHierarchy(root);

            CreateFolderIfMissing(root, "Profiles");
            CreateFolderIfMissing(root, "Materials");
            CreateFolderIfMissing(root, "Textures");
            CreateFolderIfMissing(root, "Meshes");
        }

        private static void CreateFolderHierarchy(string folderPath)
        {
            string[] parts = folderPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                CreateFolderIfMissing(current, parts[i]);
                current += "/" + parts[i];
            }
        }

        private static void CreateFolderIfMissing(string parent, string folderName)
        {
            string full = parent + "/" + folderName;
            if (!AssetDatabase.IsValidFolder(full))
                AssetDatabase.CreateFolder(parent, folderName);
        }

        private static UltraPipelineProfile CreateOrUpdateMasterProfile(string path)
        {
            var profile = AssetDatabase.LoadAssetAtPath<UltraPipelineProfile>(path);
            if (profile == null)
            {
                string[] existingGuids = AssetDatabase.FindAssets("t:UltraPipelineProfile");
                if (existingGuids != null && existingGuids.Length > 0)
                {
                    string existingPath = AssetDatabase.GUIDToAssetPath(existingGuids[0]);
                    profile = AssetDatabase.LoadAssetAtPath<UltraPipelineProfile>(existingPath);
                }
            }

            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<UltraPipelineProfile>();
                UltraPipelineProfileEditor.ApplyDefaultRayTracingSettings(profile);
                AssetDatabase.CreateAsset(profile, path);
            }
            else
            {
                EditorUtility.SetDirty(profile);
            }
            return profile;
        }

        private static void ConfigureURPAssets(UltraPipelineProfile activeProfile)
        {
            string[] rpGuids = AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset");
            UniversalRenderPipelineAsset primaryRpAsset = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;

            // If target project was a Built-in 3D project with no UniversalRenderPipelineAsset yet, create one automatically!
            if (rpGuids == null || rpGuids.Length == 0)
            {
                string rendererAssetPath = ProfilesFolder + "/UltraURP_Renderer.asset";
                string pipelineAssetPath = ProfilesFolder + "/UltraURP_PipelineAsset.asset";

                var createdRenderer = UniversalRendererData.CreateInstance<UniversalRendererData>();
                createdRenderer.name = "UltraURP_Renderer";
                AssetDatabase.CreateAsset(createdRenderer, rendererAssetPath);

                var createdRp = UniversalRenderPipelineAsset.Create(createdRenderer);
                createdRp.name = "UltraURP_PipelineAsset";
                AssetDatabase.CreateAsset(createdRp, pipelineAssetPath);
                AssetDatabase.SaveAssets();

                primaryRpAsset = createdRp;
                rpGuids = AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset");
            }

            foreach (string guid in rpGuids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var rpAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
                if (rpAsset == null) continue;

                if (primaryRpAsset == null)
                    primaryRpAsset = rpAsset;

                rpAsset.supportsHDR = true;
                rpAsset.hdrColorBufferPrecision = HDRColorBufferPrecision._64Bits;
                rpAsset.msaaSampleCount = 1;
                rpAsset.upscalingFilter = UpscalingFilterSelection.Point;
                rpAsset.supportsCameraDepthTexture = true;
                rpAsset.supportsCameraOpaqueTexture = true;
                rpAsset.shadowDistance = 55f;
                rpAsset.shadowCascadeCount = 4;
                rpAsset.mainLightShadowmapResolution = 2048;

                SerializedObject rpSO = new SerializedObject(rpAsset);
                var addModeProp = rpSO.FindProperty("m_AdditionalLightsRenderingMode");
                if (addModeProp != null) addModeProp.intValue = (int)LightRenderingMode.PerPixel;
                var addLimitProp = rpSO.FindProperty("m_AdditionalLightsPerObjectLimit");
                if (addLimitProp != null) addLimitProp.intValue = 8;
                var softShadowProp = rpSO.FindProperty("m_SoftShadowsSupported");
                if (softShadowProp != null) softShadowProp.boolValue = true;
                rpSO.ApplyModifiedPropertiesWithoutUndo();

                EditorUtility.SetDirty(rpAsset);
            }

            // Ensure URP Asset is actually assigned in GraphicsSettings and all QualitySettings levels (prevents Built-in RP magenta screen!)
            if (primaryRpAsset != null)
            {
                GraphicsSettings.defaultRenderPipeline = primaryRpAsset;

                int currentLevel = QualitySettings.GetQualityLevel();
                string[] qualityNames = QualitySettings.names;
                for (int q = 0; q < qualityNames.Length; q++)
                {
                    QualitySettings.SetQualityLevel(q, false);
                    if (QualitySettings.renderPipeline == null || !(QualitySettings.renderPipeline is UniversalRenderPipelineAsset))
                        QualitySettings.renderPipeline = primaryRpAsset;
                }
                QualitySettings.SetQualityLevel(currentLevel, false);
            }

            string[] rendererGuids = AssetDatabase.FindAssets("t:UniversalRendererData");
            foreach (string guid in rendererGuids)
            {
                string rendererPath = AssetDatabase.GUIDToAssetPath(guid);
                var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
                if (rendererData == null) continue;

                AttachFeatureToRendererData(rendererData, activeProfile);
            }
        }

        private static void AttachManagerToSceneCameras(UltraPipelineProfile activeProfile)
        {
            Camera mainCam = Camera.main;
            if (mainCam == null)
                mainCam = Object.FindFirstObjectByType<Camera>();

            if (mainCam != null)
            {
                var mgr = mainCam.GetComponent<UltraLowResPipelineManager>();
                if (mgr == null)
                    mgr = mainCam.gameObject.AddComponent<UltraLowResPipelineManager>();
                mgr.profile = activeProfile;
                mgr.SyncPipelineSettings();
                EditorUtility.SetDirty(mainCam.gameObject);
            }
        }

        private static int UpgradeBuiltInAndPinkMaterialsToURP()
        {
            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit == null)
                urpLit = Shader.Find("UltraLoFiPipeline/UltraRealisticLit");
            if (urpLit == null)
                return 0;

            Shader urpParticles = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            int upgradedCount = 0;

            string[] matGuids = AssetDatabase.FindAssets("t:Material");
            foreach (string guid in matGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/"))
                    continue;

                Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                    continue;

                string sName = mat.shader != null ? mat.shader.name : string.Empty;
                bool isBrokenOrBuiltIn =
                    mat.shader == null ||
                    sName == "Hidden/InternalErrorShader" ||
                    sName == "Standard" ||
                    sName == "Standard (Specular setup)" ||
                    sName.StartsWith("Legacy Shaders/") ||
                    sName.StartsWith("Mobile/") ||
                    sName.StartsWith("Nature/") ||
                    sName.StartsWith("HDRP/");

                if (!isBrokenOrBuiltIn)
                    continue;

                Color oldColor = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor")
                               : (mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white);
                Texture oldMainTex = mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap")
                                   : (mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null);
                Vector2 oldScale = mat.HasProperty("_MainTex") ? mat.GetTextureScale("_MainTex") : Vector2.one;
                Vector2 oldOffset = mat.HasProperty("_MainTex") ? mat.GetTextureOffset("_MainTex") : Vector2.zero;
                float oldSmooth = mat.HasProperty("_Smoothness") ? mat.GetFloat("_Smoothness")
                                : (mat.HasProperty("_Glossiness") ? mat.GetFloat("_Glossiness") : 0.35f);
                float oldMetallic = mat.HasProperty("_Metallic") ? mat.GetFloat("_Metallic") : 0.0f;
                Texture oldBump = mat.HasProperty("_BumpMap") ? mat.GetTexture("_BumpMap") : null;
                float oldBumpScale = mat.HasProperty("_BumpScale") ? mat.GetFloat("_BumpScale") : 1.0f;
                Color oldEmission = mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor") : Color.black;
                bool wasTransparent = mat.renderQueue >= (int)RenderQueue.Transparent ||
                                      (mat.HasProperty("_Mode") && mat.GetFloat("_Mode") >= 2.0f);

                mat.shader = (sName.Contains("Particle") && urpParticles != null) ? urpParticles : urpLit;

                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", oldColor);
                if (mat.HasProperty("_BaseMap") && oldMainTex != null)
                {
                    mat.SetTexture("_BaseMap", oldMainTex);
                    mat.SetTextureScale("_BaseMap", oldScale);
                    mat.SetTextureOffset("_BaseMap", oldOffset);
                }
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", oldSmooth);
                if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", oldMetallic);
                if (mat.HasProperty("_BumpMap") && oldBump != null)
                {
                    mat.SetTexture("_BumpMap", oldBump);
                    if (mat.HasProperty("_BumpScale")) mat.SetFloat("_BumpScale", oldBumpScale);
                }
                if (mat.HasProperty("_EmissionColor") && oldEmission.maxColorComponent > 0.001f)
                {
                    mat.SetColor("_EmissionColor", oldEmission);
                    mat.EnableKeyword("_EMISSION");
                }
                if (wasTransparent && mat.HasProperty("_Surface"))
                {
                    mat.SetFloat("_Surface", 1f);
                    mat.SetOverrideTag("RenderType", "Transparent");
                    mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                    mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    mat.SetFloat("_ZWrite", 0f);
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.renderQueue = (int)RenderQueue.Transparent;
                }

                EditorUtility.SetDirty(mat);
                upgradedCount++;
            }

            return upgradedCount;
        }

        private static void AttachFeatureToRendererData(UniversalRendererData rendererData, UltraPipelineProfile activeProfile)
        {
            UltraRenderPipelineFeature existingFeature = null;

            foreach (var feature in rendererData.rendererFeatures)
            {
                if (feature == null) continue;

                if (feature.GetType().Name.Contains("ScreenSpaceAmbientOcclusion"))
                {
                    feature.SetActive(false);
                    EditorUtility.SetDirty(feature);
                }

                if (feature is UltraRenderPipelineFeature ultraFeature)
                {
                    existingFeature = ultraFeature;
                }
            }

            var rtShader = AssetDatabase.LoadAssetAtPath<RayTracingShader>(RootFolder + "/Shaders/UltraHardwareRayTracing.raytrace");
            if (rtShader == null)
            {
                string[] rtGuids = AssetDatabase.FindAssets("UltraHardwareRayTracing t:RayTracingShader");
                if (rtGuids != null && rtGuids.Length > 0)
                    rtShader = AssetDatabase.LoadAssetAtPath<RayTracingShader>(AssetDatabase.GUIDToAssetPath(rtGuids[0]));
            }

            if (existingFeature == null)
            {
                existingFeature = ScriptableObject.CreateInstance<UltraRenderPipelineFeature>();
                existingFeature.name = "UltraRenderPipelineFeature";
                existingFeature.profile = activeProfile;
                existingFeature.hardwareRayTracingShader = rtShader;
                existingFeature.SetActive(true);

                AssetDatabase.AddObjectToAsset(existingFeature, rendererData);
                rendererData.rendererFeatures.Add(existingFeature);

                SerializedObject so = new SerializedObject(rendererData);
                SerializedProperty featuresProp = so.FindProperty("m_RendererFeatures");
                SerializedProperty mapProp = so.FindProperty("m_RendererFeatureMap");
                if (featuresProp != null && mapProp != null)
                {
                    mapProp.arraySize = featuresProp.arraySize;
                    for (int i = 0; i < featuresProp.arraySize; i++)
                    {
                        Object obj = featuresProp.GetArrayElementAtIndex(i).objectReferenceValue;
                        if (obj != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out _, out long localId))
                        {
                            mapProp.GetArrayElementAtIndex(i).longValue = localId;
                        }
                    }
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                existingFeature.profile = activeProfile;
                existingFeature.hardwareRayTracingShader = rtShader;
                existingFeature.SetActive(true);
                EditorUtility.SetDirty(existingFeature);
            }

            rendererData.SetDirty();
            EditorUtility.SetDirty(rendererData);
        }

        private static Material CreateOrUpdateSkyboxMaterial()
        {
            string path = MaterialsFolder + "/Mat_UltraProceduralSkybox.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader == null)
                skyShader = Shader.Find("Universal Render Pipeline/Lit");

            if (mat == null)
            {
                mat = new Material(skyShader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = skyShader;
            }

            mat.SetFloat("_SunDisk", 2f);
            mat.SetFloat("_SunSize", 0.038f);
            mat.SetFloat("_SunSizeConvergence", 6f);
            mat.SetFloat("_AtmosphereThickness", 1.18f);
            mat.SetColor("_SkyTint", new Color(0.36f, 0.52f, 0.85f, 1f));
            mat.SetColor("_GroundColor", new Color(0.24f, 0.20f, 0.17f, 1f));
            mat.SetFloat("_Exposure", 1.25f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material CreateUltraLitMaterial(
            string name,
            Shader shader,
            Color baseColor,
            float metallic,
            float smoothness,
            Texture2D albedoMap = null,
            Texture2D normalMap = null,
            float normalScale = 1.0f,
            Vector2? tiling = null,
            float sssStrength = 0.0f,
            Color? sssColor = null,
            float sssWrap = 0.45f,
            float translucencyStrength = 0.0f,
            float translucencyPower = 4.0f,
            float thickness = 0.25f,
            float clearCoatMask = 0.0f,
            float clearCoatSmoothness = 0.9f,
            Color? emissionColor = null)
        {
            string path = MaterialsFolder + "/" + name + ".mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = shader;
            }

            mat.SetColor("_BaseColor", baseColor);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetTexture("_BaseMap", albedoMap);
            mat.SetTexture("_BumpMap", normalMap);
            mat.SetFloat("_BumpScale", normalMap != null ? normalScale : 0.0f);

            if (tiling.HasValue)
            {
                mat.SetTextureScale("_BaseMap", tiling.Value);
                mat.SetTextureScale("_BumpMap", tiling.Value);
            }

            mat.SetFloat("_SubsurfaceStrength", sssStrength);
            mat.SetColor("_SubsurfaceColor", sssColor ?? new Color(1.0f, 0.28f, 0.12f, 1.0f));
            mat.SetFloat("_SubsurfaceWrap", sssWrap);
            mat.SetFloat("_TranslucencyStrength", translucencyStrength);
            mat.SetFloat("_TranslucencyPower", translucencyPower);
            mat.SetFloat("_Thickness", thickness);
            mat.SetFloat("_ClearCoatMask", clearCoatMask);
            mat.SetFloat("_ClearCoatSmoothness", clearCoatSmoothness);

            if (emissionColor.HasValue)
            {
                mat.SetColor("_EmissionColor", emissionColor.Value);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                mat.SetColor("_EmissionColor", Color.black);
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void GenerateProceduralTextures(
            out Texture2D stoneAlbedo,
            out Texture2D stoneNormal,
            out Texture2D marbleAlbedo,
            out Texture2D marbleNormal)
        {
            // High-resolution 512x512 smooth Trilinear textures so objects have zero texture pixelation when Low-Res filter is disabled!
            const int size = 512;

            Color[] sAlb = new Color[size * size];
            float[] sHeight = new float[size * size];

            for (int y = 0; y < size; y++)
            {
                float v = (float)y / size;
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size;
                    // Smooth seamless cosine-domain Perlin stone variation (no dark rectangular borders!)
                    float sx = Mathf.Cos(u * Mathf.PI * 2f) * 1.2f + 3.1f;
                    float sy = Mathf.Sin(u * Mathf.PI * 2f) * 1.2f + 5.7f;
                    float sz = Mathf.Cos(v * Mathf.PI * 2f) * 1.2f + 2.3f;

                    float n1 = Mathf.PerlinNoise(sx * 2.5f, sz * 2.5f);
                    float n2 = Mathf.PerlinNoise(sy * 6.0f, sz * 6.0f) * 0.35f;
                    float h = (n1 + n2) * 0.74f;

                    sHeight[y * size + x] = h;
                    float c = 0.84f + 0.14f * n1;
                    sAlb[y * size + x] = new Color(c, c * 0.99f, c * 0.97f, 1f);
                }
            }

            stoneAlbedo = SaveTextureAsset(TexturesFolder + "/Tex_StoneTile_Albedo.asset", size, sAlb, isNormal: false);
            stoneNormal = SaveNormalMapFromHeight(TexturesFolder + "/Tex_StoneTile_Normal.asset", size, sHeight, strength: 1.2f);

            Color[] mAlb = new Color[size * size];
            float[] mHeight = new float[size * size];

            for (int y = 0; y < size; y++)
            {
                float v = (float)y / size;
                float cv = Mathf.Cos(v * Mathf.PI * 2f);
                float sv = Mathf.Sin(v * Mathf.PI * 2f);
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size;
                    float cu = Mathf.Cos(u * Mathf.PI * 2f);
                    float su = Mathf.Sin(u * Mathf.PI * 2f);

                    // 100% seamlessly periodic 2D Perlin marble veins (zero UV tile border seams!)
                    float px = cu * 1.1f + cv * 0.45f + 4.3f;
                    float py = su * 1.1f + sv * 0.45f + 7.1f;
                    float turb = Mathf.PerlinNoise(px * 1.6f, py * 1.6f) * 2.1f
                               + Mathf.PerlinNoise(px * 3.4f + 5.2f, py * 3.4f + 2.8f) * 0.55f;
                    float phase = cu * 1.25f + sv * 1.25f + turb;
                    float vein = Mathf.Sin(phase * Mathf.PI) * 0.5f + 0.5f;
                    vein = Mathf.Pow(vein, 0.75f);

                    mHeight[y * size + x] = vein;
                    float lum = Mathf.Lerp(0.84f, 0.98f, vein);
                    mAlb[y * size + x] = new Color(lum, lum * 0.98f, lum * 0.95f, 1f);
                }
            }

            marbleAlbedo = SaveTextureAsset(TexturesFolder + "/Tex_VeinedMarble_Albedo.asset", size, mAlb, isNormal: false);
            marbleNormal = SaveNormalMapFromHeight(TexturesFolder + "/Tex_VeinedMarble_Normal.asset", size, mHeight, strength: 0.65f);
        }

        private static Texture2D SaveTextureAsset(string path, int size, Color[] pixels, bool isNormal)
        {
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null || tex.width != size || tex.height != size)
            {
                if (tex != null)
                    AssetDatabase.DeleteAsset(path);

                tex = new Texture2D(size, size, TextureFormat.RGBA32, true, isNormal);
                tex.wrapMode = TextureWrapMode.Repeat;
                tex.filterMode = FilterMode.Trilinear;
                tex.anisoLevel = 8;
                tex.SetPixels(pixels);
                tex.Apply(true, false);
                AssetDatabase.CreateAsset(tex, path);
            }
            else
            {
                tex.filterMode = FilterMode.Trilinear;
                tex.anisoLevel = 8;
                tex.SetPixels(pixels);
                tex.Apply(true, false);
                EditorUtility.SetDirty(tex);
            }
            return tex;
        }

        private static Texture2D SaveNormalMapFromHeight(string path, int size, float[] height, float strength)
        {
            Color[] normPixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                int yUp = (y + 1) % size;
                int yDn = (y - 1 + size) % size;
                for (int x = 0; x < size; x++)
                {
                    int xRt = (x + 1) % size;
                    int xLt = (x - 1 + size) % size;

                    float dx = (height[y * size + xRt] - height[y * size + xLt]) * strength;
                    float dy = (height[yUp * size + x] - height[yDn * size + x]) * strength;
                    Vector3 n = new Vector3(-dx, -dy, 1.0f).normalized;

                    normPixels[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1.0f);
                }
            }
            return SaveTextureAsset(path, size, normPixels, isNormal: true);
        }

        private static Mesh GenerateOrLoadTorusKnotMesh(string assetPath)
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (mesh == null)
            {
                mesh = new Mesh { name = "UltraTorusKnotSculpture" };
                AssetDatabase.CreateAsset(mesh, assetPath);
            }
            else
            {
                mesh.Clear();
            }

            const int tubularSegments = 160;
            const int radialSegments = 32;
            const float p = 2f;
            const float q = 3f;
            const float radius = 0.52f;
            const float tubeRadius = 0.19f;

            int vertCount = (tubularSegments + 1) * (radialSegments + 1);
            var vertices = new Vector3[vertCount];
            var normals = new Vector3[vertCount];
            var tangents = new Vector4[vertCount];
            var uvs = new Vector2[vertCount];
            var triangles = new int[tubularSegments * radialSegments * 6];

            Vector3 SampleKnotCenter(float u)
            {
                float cu = Mathf.Cos(p * u);
                float su = Mathf.Sin(p * u);
                float quOverP = (q / p) * p * u;
                float cs = Mathf.Cos(quOverP);
                float r = radius * (2.0f + cs) * 0.5f;
                return new Vector3(r * cu, r * su, radius * Mathf.Sin(quOverP) * 0.55f);
            }

            int vIdx = 0;
            for (int i = 0; i <= tubularSegments; i++)
            {
                float uFrac = (float)i / tubularSegments;
                float u = uFrac * Mathf.PI * 2f;
                Vector3 p1 = SampleKnotCenter(u);
                Vector3 p2 = SampleKnotCenter(u + 0.005f);
                Vector3 T = (p2 - p1).normalized;
                Vector3 N = (p2 + p1).normalized;
                Vector3 B = Vector3.Cross(T, N).normalized;
                N = Vector3.Cross(B, T).normalized;

                for (int j = 0; j <= radialSegments; j++)
                {
                    float vFrac = (float)j / radialSegments;
                    float v = vFrac * Mathf.PI * 2f;
                    float cx = -tubeRadius * Mathf.Cos(v);
                    float cy = tubeRadius * Mathf.Sin(v);

                    Vector3 normal = (N * Mathf.Cos(v) * -1f + B * Mathf.Sin(v)).normalized;
                    vertices[vIdx] = p1 + N * cx + B * cy;
                    normals[vIdx] = normal;
                    tangents[vIdx] = new Vector4(T.x, T.y, T.z, 1.0f);
                    uvs[vIdx] = new Vector2(uFrac * 6f, vFrac * 2f);
                    vIdx++;
                }
            }

            int tIdx = 0;
            for (int j = 1; j <= tubularSegments; j++)
            {
                for (int i = 1; i <= radialSegments; i++)
                {
                    int a = (radialSegments + 1) * (j - 1) + (i - 1);
                    int b = (radialSegments + 1) * j + (i - 1);
                    int c = (radialSegments + 1) * j + i;
                    int d = (radialSegments + 1) * (j - 1) + i;

                    triangles[tIdx++] = a;
                    triangles[tIdx++] = d;
                    triangles[tIdx++] = b;

                    triangles[tIdx++] = b;
                    triangles[tIdx++] = d;
                    triangles[tIdx++] = c;
                }
            }

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.tangents = tangents;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static Mesh GenerateOrLoadHighPolySphereMesh(string assetPath)
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (mesh == null)
            {
                mesh = new Mesh { name = "UltraHighPolySphere" };
                AssetDatabase.CreateAsset(mesh, assetPath);
            }
            else
            {
                mesh.Clear();
            }

            const int lonSegments = 96;
            const int latSegments = 64;
            const float radius = 0.5f;

            int vertCount = (lonSegments + 1) * (latSegments + 1);
            var vertices = new Vector3[vertCount];
            var normals = new Vector3[vertCount];
            var tangents = new Vector4[vertCount];
            var uvs = new Vector2[vertCount];
            var triangles = new int[lonSegments * latSegments * 6];

            int vIdx = 0;
            for (int lat = 0; lat <= latSegments; lat++)
            {
                float vFrac = (float)lat / latSegments;
                float theta = vFrac * Mathf.PI;
                float sinTheta = Mathf.Sin(theta);
                float cosTheta = Mathf.Cos(theta);

                for (int lon = 0; lon <= lonSegments; lon++)
                {
                    float uFrac = (float)lon / lonSegments;
                    float phi = uFrac * Mathf.PI * 2f;
                    float sinPhi = Mathf.Sin(phi);
                    float cosPhi = Mathf.Cos(phi);

                    Vector3 normal = new Vector3(sinTheta * cosPhi, cosTheta, sinTheta * sinPhi).normalized;
                    Vector3 tangent = new Vector3(-sinPhi, 0f, cosPhi).normalized;

                    vertices[vIdx] = normal * radius;
                    normals[vIdx] = normal;
                    tangents[vIdx] = new Vector4(tangent.x, tangent.y, tangent.z, 1.0f);
                    uvs[vIdx] = new Vector2(uFrac, 1.0f - vFrac);
                    vIdx++;
                }
            }

            int tIdx = 0;
            for (int lat = 0; lat < latSegments; lat++)
            {
                for (int lon = 0; lon < lonSegments; lon++)
                {
                    int current = lat * (lonSegments + 1) + lon;
                    int next = current + lonSegments + 1;

                    triangles[tIdx++] = current;
                    triangles[tIdx++] = current + 1;
                    triangles[tIdx++] = next;

                    triangles[tIdx++] = current + 1;
                    triangles[tIdx++] = next + 1;
                    triangles[tIdx++] = next;
                }
            }

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.tangents = tangents;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static GameObject CreatePrimitive(string name, PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Quaternion rot, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null && mat != null)
                renderer.sharedMaterial = mat;

            return go;
        }

        private static GameObject CreateMeshObject(string name, Mesh mesh, Transform parent, Vector3 pos, Vector3 scale, Quaternion rot, Material mat)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.On;
            mr.receiveShadows = true;
            return go;
        }

        private static void CreatePillarAssembly(string name, Transform parent, Vector3 basePos, Material mat)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = basePos;

            CreatePrimitive("Base_Plinth", PrimitiveType.Cube, root.transform,
                new Vector3(0f, 0.25f, 0f), new Vector3(1.25f, 0.5f, 1.25f), Quaternion.identity, mat);
            CreatePrimitive("Column_Shaft", PrimitiveType.Cylinder, root.transform,
                new Vector3(0f, 3.25f, 0f), new Vector3(0.88f, 2.75f, 0.88f), Quaternion.identity, mat);
            CreatePrimitive("Capital_Crown", PrimitiveType.Cube, root.transform,
                new Vector3(0f, 6.0f, 0f), new Vector3(1.3f, 0.45f, 1.3f), Quaternion.identity, mat);
        }

        private static void CreatePedestal(string name, Transform parent, Vector3 basePos, Material mat)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = basePos;

            CreatePrimitive("Pedestal_Base", PrimitiveType.Cube, root.transform,
                new Vector3(0f, 0.15f, 0f), new Vector3(1.4f, 0.3f, 1.4f), Quaternion.identity, mat);
            CreatePrimitive("Pedestal_Column", PrimitiveType.Cube, root.transform,
                new Vector3(0f, 0.65f, 0f), new Vector3(1.0f, 0.8f, 1.0f), Quaternion.identity, mat);
            CreatePrimitive("Pedestal_Cap", PrimitiveType.Cube, root.transform,
                new Vector3(0f, 1.12f, 0f), new Vector3(1.3f, 0.18f, 1.3f), Quaternion.identity, mat);
        }

        private static Light CreatePointLight(string name, Transform parent, Vector3 pos, Color color, float intensity, float range, bool shadows)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;

            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;

            var addData = go.AddComponent<UniversalAdditionalLightData>();
            addData.usePipelineSettings = true;
            return light;
        }
    }
}
