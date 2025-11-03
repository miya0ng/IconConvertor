using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

/// <summary>
/// 프리팹 썸네일 생성기 - 향상된 버전
///
/// 주요 개선사항:
/// - 실시간 미리보기
/// - 3점 조명 시스템
/// - 안티앨리어싱 지원
/// - 취소 기능
/// - 스마트 스케일링
/// - 설정 프리셋
/// </summary>
public class PrefabThumbnailGeneratorAdvanced : EditorWindow
{
    #region Nested Classes

    [System.Serializable]
    public class ThumbnailSettings
    {
        public int resolution = 512;
        public float maxSize = 2f;
        public float minSize = 1f;
        public ScalingMode scalingMode = ScalingMode.Uniform;
        public Vector3 cameraAngle = new Vector3(15f, -30f, 0f);
        public float cameraDistance = 5f;
        public Color backgroundColor = Color.magenta;
        public bool enableLighting = true;
        public bool enableAntiAliasing = true;
        public bool enableShadows = false;
        public PivotAlignment pivotAlignment = PivotAlignment.Center;
    }

    public enum ScalingMode
    {
        Uniform,        // 모든 축 동일 스케일
        PerAxis,        // 각 축별 독립 조정
        FitToSquare     // 정사각형 영역에 맞춤
    }

    public enum PivotAlignment
    {
        Center,
        Bottom,
        Custom
    }

    #endregion

    #region Fields

    private GameObject targetProp;
    private List<GameObject> prefabsToProcess = new List<GameObject>();
    private string outputFolder = "Assets/GeneratedIcons";

    private ThumbnailSettings settings = new ThumbnailSettings();

    // 렌더링
    private Camera renderCamera;
    private GameObject lightingRig;
    private Light mainLight;
    private Light fillLight;
    private Light rimLight;

    // 미리보기
    private bool showPreview = false;
    private Texture2D previewTexture;
    private GameObject previewInstance;
    private int selectedPrefabIndex = -1;

    // UI
    private Vector2 scrollPosition;
    private bool showAdvancedSettings = false;
    private bool isProcessing = false;
    private bool cancelRequested = false;

    // 탭
    private int selectedTab = 0;
    private string[] tabNames = { "기본 설정", "고급 설정", "미리보기" };

    #endregion

    #region Unity Menu

    [MenuItem("Tools/Prefab Thumbnail Generator (Advanced)")]
    public static void ShowWindow()
    {
        var window = GetWindow<PrefabThumbnailGeneratorAdvanced>("Thumbnail Generator+");
        window.minSize = new Vector2(500, 700);
    }

    #endregion

    #region Unity Lifecycle

    private void OnDestroy()
    {
        CleanupPreview();
        CleanupRenderCamera();
    }

    #endregion

    #region GUI

    private void OnGUI()
    {
        GUILayout.Label("프리팹 썸네일 생성기 (고급)", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // 탭 선택
        selectedTab = GUILayout.Toolbar(selectedTab, tabNames);
        EditorGUILayout.Space();

        switch (selectedTab)
        {
            case 0:
                DrawBasicSettingsTab();
                break;
            case 1:
                DrawAdvancedSettingsTab();
                break;
            case 2:
                DrawPreviewTab();
                break;
        }

        EditorGUILayout.Space();
        DrawActionButtons();

        if (isProcessing)
        {
            EditorGUILayout.HelpBox("처리 중입니다. 잠시만 기다려주세요...", MessageType.Info);
            if (GUILayout.Button("취소"))
            {
                cancelRequested = true;
            }
        }
    }

    private void DrawBasicSettingsTab()
    {
        GUILayout.Label("기본 설정", EditorStyles.boldLabel);

        targetProp = (GameObject)EditorGUILayout.ObjectField(
            new GUIContent("타겟 Prop", "썸네일 촬영 시 프리팹이 배치될 씬 오브젝트"),
            targetProp,
            typeof(GameObject),
            true
        );

        EditorGUILayout.Space();

        outputFolder = EditorGUILayout.TextField("출력 폴더", outputFolder);
        settings.resolution = EditorGUILayout.IntSlider("해상도", settings.resolution, 128, 2048);

        EditorGUILayout.Space();
        GUILayout.Label("크기 조정", EditorStyles.boldLabel);

        settings.scalingMode = (ScalingMode)EditorGUILayout.EnumPopup("스케일 모드", settings.scalingMode);
        settings.maxSize = EditorGUILayout.Slider("최대 크기", settings.maxSize, 0.5f, 10f);
        settings.minSize = EditorGUILayout.Slider("최소 크기", settings.minSize, 0.1f, 5f);
        settings.pivotAlignment = (PivotAlignment)EditorGUILayout.EnumPopup("정렬 기준점", settings.pivotAlignment);

        EditorGUILayout.Space();
        GUILayout.Label("프리팹 목록", EditorStyles.boldLabel);

        DrawPrefabList();
    }

    private void DrawAdvancedSettingsTab()
    {
        GUILayout.Label("고급 설정", EditorStyles.boldLabel);

        EditorGUILayout.Space();
        GUILayout.Label("카메라 설정", EditorStyles.boldLabel);

        settings.cameraDistance = EditorGUILayout.Slider("카메라 거리", settings.cameraDistance, 2f, 20f);
        settings.cameraAngle = EditorGUILayout.Vector3Field("카메라 각도", settings.cameraAngle);

        EditorGUILayout.Space();
        GUILayout.Label("렌더링 설정", EditorStyles.boldLabel);

        settings.backgroundColor = EditorGUILayout.ColorField("배경색 (투명화)", settings.backgroundColor);
        settings.enableAntiAliasing = EditorGUILayout.Toggle("안티앨리어싱", settings.enableAntiAliasing);
        settings.enableLighting = EditorGUILayout.Toggle("조명 활성화", settings.enableLighting);
        settings.enableShadows = EditorGUILayout.Toggle("그림자 활성화", settings.enableShadows);

        EditorGUILayout.Space();

        if (settings.enableLighting)
        {
            EditorGUILayout.HelpBox(
                "3점 조명 시스템:\n" +
                "- Main Light: 주 광원 (45도 각도)\n" +
                "- Fill Light: 보조 광원 (그림자 완화)\n" +
                "- Rim Light: 테두리 강조",
                MessageType.Info
            );
        }

        EditorGUILayout.Space();
        GUILayout.Label("프리셋", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("아이템 아이콘"))
        {
            LoadPreset_ItemIcon();
        }
        if (GUILayout.Button("건물 썸네일"))
        {
            LoadPreset_Building();
        }
        if (GUILayout.Button("캐릭터"))
        {
            LoadPreset_Character();
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawPreviewTab()
    {
        GUILayout.Label("미리보기", EditorStyles.boldLabel);

        if (prefabsToProcess.Count == 0)
        {
            EditorGUILayout.HelpBox("프리팹 목록에 아이템을 추가해주세요.", MessageType.Warning);
            return;
        }

        // 프리팹 선택
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("미리보기할 프리팹:", GUILayout.Width(120));

        string[] prefabNames = new string[prefabsToProcess.Count];
        for (int i = 0; i < prefabsToProcess.Count; i++)
        {
            prefabNames[i] = prefabsToProcess[i] != null ? prefabsToProcess[i].name : "null";
        }

        int newIndex = EditorGUILayout.Popup(selectedPrefabIndex, prefabNames);
        if (newIndex != selectedPrefabIndex)
        {
            selectedPrefabIndex = newIndex;
            showPreview = false; // 재생성 필요
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        // 미리보기 버튼
        if (GUILayout.Button(showPreview ? "미리보기 갱신" : "미리보기 생성", GUILayout.Height(30)))
        {
            if (selectedPrefabIndex >= 0 && selectedPrefabIndex < prefabsToProcess.Count)
            {
                GeneratePreview(prefabsToProcess[selectedPrefabIndex]);
            }
        }

        EditorGUILayout.Space();

        // 미리보기 이미지 표시
        if (showPreview && previewTexture != null)
        {
            GUILayout.Label("미리보기 결과:", EditorStyles.boldLabel);

            // 미리보기 크기 계산 (최대 400x400)
            float maxPreviewSize = 400f;
            float previewSize = Mathf.Min(maxPreviewSize, EditorGUIUtility.currentViewWidth - 40);

            Rect previewRect = GUILayoutUtility.GetRect(previewSize, previewSize);
            EditorGUI.DrawPreviewTexture(previewRect, previewTexture);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("해상도:", $"{previewTexture.width} x {previewTexture.height}");
        }
        else if (showPreview)
        {
            EditorGUILayout.HelpBox("미리보기를 생성할 수 없습니다.", MessageType.Error);
        }
    }

    private void DrawPrefabList()
    {
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("프리팹 추가", GUILayout.Width(100)))
        {
            prefabsToProcess.Add(null);
        }
        if (GUILayout.Button("선택 일괄 추가", GUILayout.Width(100)))
        {
            AddSelectedPrefabs();
        }
        if (GUILayout.Button("목록 초기화", GUILayout.Width(100)))
        {
            prefabsToProcess.Clear();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(200));

        for (int i = prefabsToProcess.Count - 1; i >= 0; i--)
        {
            EditorGUILayout.BeginHorizontal();

            prefabsToProcess[i] = (GameObject)EditorGUILayout.ObjectField(
                $"Prefab {i + 1}",
                prefabsToProcess[i],
                typeof(GameObject),
                false
            );

            if (GUILayout.Button("제거", GUILayout.Width(50)))
            {
                prefabsToProcess.RemoveAt(i);
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.HelpBox($"등록된 프리팹: {prefabsToProcess.Count}개", MessageType.Info);
    }

    private void DrawActionButtons()
    {
        EditorGUI.BeginDisabledGroup(isProcessing || prefabsToProcess.Count == 0);

        if (GUILayout.Button("썸네일 생성", GUILayout.Height(40)))
        {
            GenerateThumbnails();
        }

        EditorGUI.EndDisabledGroup();
    }

    #endregion

    #region Main Logic

    private void GenerateThumbnails()
    {
        if (targetProp == null)
        {
            EditorUtility.DisplayDialog("오류", "타겟 Prop을 지정해주세요.", "확인");
            return;
        }

        if (!Directory.Exists(outputFolder))
        {
            Directory.CreateDirectory(outputFolder);
        }

        isProcessing = true;
        cancelRequested = false;

        try
        {
            SetupRenderCamera();
            if (settings.enableLighting)
            {
                SetupLighting();
            }

            int successCount = 0;
            int totalCount = prefabsToProcess.Count;

            for (int i = 0; i < totalCount; i++)
            {
                if (cancelRequested)
                {
                    Debug.Log("사용자에 의해 취소되었습니다.");
                    break;
                }

                GameObject prefab = prefabsToProcess[i];

                if (prefab == null)
                {
                    Debug.LogWarning($"프리팹 {i + 1}이(가) null입니다. 건너뜁니다.");
                    continue;
                }

                float progress = (float)i / totalCount;
                bool cancelled = EditorUtility.DisplayCancelableProgressBar(
                    "썸네일 생성 중",
                    $"처리 중: {prefab.name} ({i + 1}/{totalCount})",
                    progress
                );

                if (cancelled)
                {
                    cancelRequested = true;
                    break;
                }

                if (GenerateSingleThumbnail(prefab))
                {
                    successCount++;
                }
            }

            EditorUtility.ClearProgressBar();

            string message = cancelRequested
                ? $"처리 취소됨.\n완료: {successCount}/{totalCount}"
                : $"썸네일 생성 완료!\n성공: {successCount}/{totalCount}\n저장 위치: {outputFolder}";

            EditorUtility.DisplayDialog("완료", message, "확인");

            AssetDatabase.Refresh();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"썸네일 생성 중 오류 발생: {e.Message}");
            EditorUtility.DisplayDialog("오류", $"썸네일 생성 중 오류가 발생했습니다.\n{e.Message}", "확인");
        }
        finally
        {
            CleanupRenderCamera();
            isProcessing = false;
            cancelRequested = false;
        }
    }

    private bool GenerateSingleThumbnail(GameObject prefab)
    {
        GameObject instance = null;

        try
        {
            // 1. 인스턴스화
            instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            instance.transform.SetParent(targetProp.transform);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            // 2. 크기 정규화
            NormalizeObjectSize(instance);

            // 3. 정렬 조정
            AdjustAlignment(instance);

            // 4. 카메라 위치 조정
            PositionCamera(instance);

            // 5. 스크린샷 촬영
            Texture2D screenshot = CaptureScreenshot();

            // 6. 투명도 처리
            Texture2D transparentTexture = ConvertToTransparent(screenshot);

            // 7. 저장
            string fileName = $"{prefab.name}_thumbnail.png";
            string filePath = Path.Combine(outputFolder, fileName);
            SaveTextureToPNG(transparentTexture, filePath);

            // 8. 정리
            DestroyImmediate(screenshot);
            DestroyImmediate(transparentTexture);

            Debug.Log($"✓ 썸네일 생성 완료: {fileName}");
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"✗ 프리팹 '{prefab.name}' 처리 중 오류: {e.Message}");
            return false;
        }
        finally
        {
            if (instance != null)
            {
                DestroyImmediate(instance);
            }
        }
    }

    #endregion

    #region Preview

    private void GeneratePreview(GameObject prefab)
    {
        if (prefab == null)
        {
            showPreview = false;
            return;
        }

        CleanupPreview();

        if (targetProp == null)
        {
            EditorUtility.DisplayDialog("오류", "타겟 Prop을 지정해주세요.", "확인");
            return;
        }

        try
        {
            SetupRenderCamera();
            if (settings.enableLighting)
            {
                SetupLighting();
            }

            // 미리보기용 인스턴스 생성
            previewInstance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            previewInstance.transform.SetParent(targetProp.transform);
            previewInstance.transform.localPosition = Vector3.zero;
            previewInstance.transform.localRotation = Quaternion.identity;
            previewInstance.transform.localScale = Vector3.one;

            NormalizeObjectSize(previewInstance);
            AdjustAlignment(previewInstance);
            PositionCamera(previewInstance);

            Texture2D screenshot = CaptureScreenshot();
            previewTexture = ConvertToTransparent(screenshot);
            DestroyImmediate(screenshot);

            showPreview = true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"미리보기 생성 중 오류: {e.Message}");
            CleanupPreview();
        }
    }

    private void CleanupPreview()
    {
        if (previewInstance != null)
        {
            DestroyImmediate(previewInstance);
            previewInstance = null;
        }

        if (previewTexture != null)
        {
            DestroyImmediate(previewTexture);
            previewTexture = null;
        }

        CleanupRenderCamera();
    }

    #endregion

    #region Size Normalization

    private void NormalizeObjectSize(GameObject obj)
    {
        Bounds bounds = CalculateBounds(obj);

        if (bounds.size == Vector3.zero)
        {
            Debug.LogWarning($"오브젝트 '{obj.name}'의 크기를 계산할 수 없습니다.");
            return;
        }

        Vector3 size = bounds.size;
        float scaleFactor = 1f;

        switch (settings.scalingMode)
        {
            case ScalingMode.Uniform:
                scaleFactor = CalculateUniformScale(size);
                obj.transform.localScale = Vector3.one * scaleFactor;
                break;

            case ScalingMode.PerAxis:
                Vector3 perAxisScale = CalculatePerAxisScale(size);
                obj.transform.localScale = perAxisScale;
                break;

            case ScalingMode.FitToSquare:
                scaleFactor = CalculateFitToSquareScale(size);
                obj.transform.localScale = Vector3.one * scaleFactor;
                break;
        }
    }

    private float CalculateUniformScale(Vector3 size)
    {
        float maxDimension = Mathf.Max(size.x, size.y, size.z);
        float scaleFactor = 1f;

        if (maxDimension > settings.maxSize)
        {
            scaleFactor = settings.maxSize / maxDimension;
        }
        else if (maxDimension < settings.minSize)
        {
            scaleFactor = settings.minSize / maxDimension;
        }

        return scaleFactor;
    }

    private Vector3 CalculatePerAxisScale(Vector3 size)
    {
        Vector3 scale = Vector3.one;

        scale.x = Mathf.Clamp(settings.maxSize / size.x, settings.minSize / size.x, settings.maxSize / size.x);
        scale.y = Mathf.Clamp(settings.maxSize / size.y, settings.minSize / size.y, settings.maxSize / size.y);
        scale.z = Mathf.Clamp(settings.maxSize / size.z, settings.minSize / size.z, settings.maxSize / size.z);

        return scale;
    }

    private float CalculateFitToSquareScale(Vector3 size)
    {
        // XZ 평면 기준으로 정사각형에 맞춤
        float maxXZ = Mathf.Max(size.x, size.z);
        return Mathf.Clamp(settings.maxSize / maxXZ, settings.minSize / maxXZ, settings.maxSize / maxXZ);
    }

    private void AdjustAlignment(GameObject obj)
    {
        if (settings.pivotAlignment == PivotAlignment.Center)
        {
            return; // 기본 중앙 정렬
        }

        Bounds bounds = CalculateBounds(obj);

        if (settings.pivotAlignment == PivotAlignment.Bottom)
        {
            // 바운드의 바닥이 부모의 중심에 오도록
            float offset = bounds.center.y - bounds.min.y;
            obj.transform.localPosition = new Vector3(0, -offset, 0);
        }
    }

    private Bounds CalculateBounds(GameObject obj)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();

        if (renderers.Length == 0)
        {
            // Renderer가 없으면 Collider 확인
            Collider[] colliders = obj.GetComponentsInChildren<Collider>();
            if (colliders.Length > 0)
            {
                Bounds bounds = colliders[0].bounds;
                foreach (Collider col in colliders)
                {
                    bounds.Encapsulate(col.bounds);
                }
                return bounds;
            }

            return new Bounds(obj.transform.position, Vector3.one);
        }

        Bounds result = renderers[0].bounds;
        foreach (Renderer renderer in renderers)
        {
            result.Encapsulate(renderer.bounds);
        }

        return result;
    }

    #endregion

    #region Camera & Lighting

    private void SetupRenderCamera()
    {
        if (renderCamera != null)
        {
            return;
        }

        GameObject cameraObj = new GameObject("ThumbnailCamera");
        renderCamera = cameraObj.AddComponent<Camera>();

        renderCamera.clearFlags = CameraClearFlags.SolidColor;
        renderCamera.backgroundColor = settings.backgroundColor;
        renderCamera.orthographic = false;
        renderCamera.fieldOfView = 30f;
        renderCamera.nearClipPlane = 0.1f;
        renderCamera.farClipPlane = 100f;

        if (settings.enableAntiAliasing)
        {
            renderCamera.allowMSAA = true;
        }
    }

    private void SetupLighting()
    {
        if (lightingRig != null)
        {
            return;
        }

        lightingRig = new GameObject("LightingRig");

        // Main Light (키 라이트) - 45도 각도에서
        GameObject mainLightObj = new GameObject("MainLight");
        mainLightObj.transform.SetParent(lightingRig.transform);
        mainLight = mainLightObj.AddComponent<Light>();
        mainLight.type = LightType.Directional;
        mainLight.intensity = 1.0f;
        mainLight.color = Color.white;
        mainLight.transform.rotation = Quaternion.Euler(45f, -30f, 0f);

        if (settings.enableShadows)
        {
            mainLight.shadows = LightShadows.Soft;
        }

        // Fill Light (필 라이트) - 그림자 완화
        GameObject fillLightObj = new GameObject("FillLight");
        fillLightObj.transform.SetParent(lightingRig.transform);
        fillLight = fillLightObj.AddComponent<Light>();
        fillLight.type = LightType.Directional;
        fillLight.intensity = 0.4f;
        fillLight.color = new Color(0.8f, 0.8f, 1f); // 약간 푸른빛
        fillLight.transform.rotation = Quaternion.Euler(-20f, 120f, 0f);

        // Rim Light (림 라이트) - 테두리 강조
        GameObject rimLightObj = new GameObject("RimLight");
        rimLightObj.transform.SetParent(lightingRig.transform);
        rimLight = rimLightObj.AddComponent<Light>();
        rimLight.type = LightType.Directional;
        rimLight.intensity = 0.6f;
        rimLight.color = new Color(1f, 0.95f, 0.8f); // 따뜻한 색
        rimLight.transform.rotation = Quaternion.Euler(30f, 160f, 0f);
    }

    private void PositionCamera(GameObject target)
    {
        Bounds bounds = CalculateBounds(target);
        Vector3 center = bounds.center;

        renderCamera.transform.position = center + Quaternion.Euler(settings.cameraAngle) * Vector3.back * settings.cameraDistance;
        renderCamera.transform.LookAt(center);

        // FOV 자동 조정
        float maxDimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        float fov = 2f * Mathf.Atan(maxDimension / (2f * settings.cameraDistance)) * Mathf.Rad2Deg;
        renderCamera.fieldOfView = Mathf.Clamp(fov * 1.3f, 15f, 60f); // 여유 30%
    }

    private void CleanupRenderCamera()
    {
        if (renderCamera != null)
        {
            DestroyImmediate(renderCamera.gameObject);
            renderCamera = null;
        }

        if (lightingRig != null)
        {
            DestroyImmediate(lightingRig);
            lightingRig = null;
            mainLight = null;
            fillLight = null;
            rimLight = null;
        }
    }

    #endregion

    #region Screenshot & Image Processing

    private Texture2D CaptureScreenshot()
    {
        int msaa = settings.enableAntiAliasing ? 4 : 1;
        RenderTexture rt = new RenderTexture(settings.resolution, settings.resolution, 24);
        rt.antiAliasing = msaa;

        renderCamera.targetTexture = rt;

        Texture2D screenshot = new Texture2D(settings.resolution, settings.resolution, TextureFormat.RGBA32, false);

        renderCamera.Render();

        RenderTexture.active = rt;
        screenshot.ReadPixels(new Rect(0, 0, settings.resolution, settings.resolution), 0, 0);
        screenshot.Apply();

        renderCamera.targetTexture = null;
        RenderTexture.active = null;
        DestroyImmediate(rt);

        return screenshot;
    }

    private Texture2D ConvertToTransparent(Texture2D source)
    {
        Texture2D result = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        Color[] pixels = source.GetPixels();

        Color targetColor = settings.backgroundColor;
        float threshold = 0.1f;

        for (int i = 0; i < pixels.Length; i++)
        {
            Color pixel = pixels[i];

            // 색상 거리 계산
            float distance = Vector3.Distance(
                new Vector3(pixel.r, pixel.g, pixel.b),
                new Vector3(targetColor.r, targetColor.g, targetColor.b)
            );

            // 임계값보다 가까우면 투명도 조정
            if (distance < threshold)
            {
                // 그라데이션 투명도 (부드러운 경계)
                float alpha = Mathf.Clamp01(distance / threshold);
                pixels[i] = new Color(pixel.r, pixel.g, pixel.b, pixel.a * alpha);
            }
        }

        result.SetPixels(pixels);
        result.Apply();

        return result;
    }

    private void SaveTextureToPNG(Texture2D texture, string filePath)
    {
        byte[] bytes = texture.EncodeToPNG();
        File.WriteAllBytes(filePath, bytes);
    }

    #endregion

    #region Presets

    private void LoadPreset_ItemIcon()
    {
        settings.resolution = 256;
        settings.maxSize = 2f;
        settings.minSize = 1f;
        settings.scalingMode = ScalingMode.Uniform;
        settings.cameraAngle = new Vector3(15f, -30f, 0f);
        settings.cameraDistance = 5f;
        settings.pivotAlignment = PivotAlignment.Center;
        settings.enableLighting = true;
        settings.enableAntiAliasing = true;
        settings.enableShadows = false;

        Debug.Log("프리셋 적용: 아이템 아이콘");
    }

    private void LoadPreset_Building()
    {
        settings.resolution = 512;
        settings.maxSize = 2f;
        settings.minSize = 1f;
        settings.scalingMode = ScalingMode.FitToSquare;
        settings.cameraAngle = new Vector3(30f, -45f, 0f);
        settings.cameraDistance = 7f;
        settings.pivotAlignment = PivotAlignment.Bottom;
        settings.enableLighting = true;
        settings.enableAntiAliasing = true;
        settings.enableShadows = true;

        Debug.Log("프리셋 적용: 건물 썸네일");
    }

    private void LoadPreset_Character()
    {
        settings.resolution = 512;
        settings.maxSize = 2f;
        settings.minSize = 1.5f;
        settings.scalingMode = ScalingMode.Uniform;
        settings.cameraAngle = new Vector3(10f, 0f, 0f);
        settings.cameraDistance = 4f;
        settings.pivotAlignment = PivotAlignment.Bottom;
        settings.enableLighting = true;
        settings.enableAntiAliasing = true;
        settings.enableShadows = false;

        Debug.Log("프리셋 적용: 캐릭터");
    }

    #endregion

    #region Helper Methods

    private void AddSelectedPrefabs()
    {
        Object[] selectedObjects = Selection.objects;
        int addedCount = 0;

        foreach (Object obj in selectedObjects)
        {
            if (obj is GameObject)
            {
                GameObject go = obj as GameObject;

                if (PrefabUtility.GetPrefabAssetType(go) != PrefabAssetType.NotAPrefab)
                {
                    if (!prefabsToProcess.Contains(go))
                    {
                        prefabsToProcess.Add(go);
                        addedCount++;
                    }
                }
            }
        }

        if (addedCount > 0)
        {
            Debug.Log($"{addedCount}개의 프리팹이 목록에 추가되었습니다.");
        }
        else
        {
            EditorUtility.DisplayDialog("알림", "프리팹을 선택해주세요.", "확인");
        }
    }

    #endregion
}
