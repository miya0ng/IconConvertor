using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

/// <summary>
/// 프리팹을 일정한 크기로 조정하여 썸네일 아이콘을 생성하는 에디터 툴
/// - 유니티 크기 2를 넘지 않도록 제한
/// - 너무 작은 오브젝트는 1x1로 키우기
/// - 마젠타 컬러를 투명화하여 PNG로 저장
/// </summary>
public class PrefabThumbnailGenerator : EditorWindow
{
    #region Fields

    private GameObject targetProp;
    private List<GameObject> prefabsToProcess = new List<GameObject>();
    private string outputFolder = "Assets/GeneratedIcons";
    private int thumbnailResolution = 512;
    private Camera renderCamera;
    private Color backgroundColor = Color.magenta; // 투명화할 배경색

    // 크기 제한 설정
    private float maxSize = 2f;        // 최대 크기 제한
    private float minSize = 1f;        // 최소 크기 (1x1)
    private bool autoScale = true;     // 자동 스케일 조정

    // 카메라 설정
    private float cameraDistance = 5f;
    private Vector3 cameraAngle = new Vector3(15f, -30f, 0f); // 아이소메트릭 스타일 각도

    private Vector2 scrollPosition;
    private bool isProcessing = false;

    #endregion

    #region Unity Menu

    [MenuItem("Tools/Prefab Thumbnail Generator")]
    public static void ShowWindow()
    {
        var window = GetWindow<PrefabThumbnailGenerator>("Thumbnail Generator");
        window.minSize = new Vector2(400, 600);
    }

    #endregion

    #region GUI

    private void OnGUI()
    {
        GUILayout.Label("프리팹 썸네일 생성기", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        DrawSettingsSection();
        EditorGUILayout.Space();

        DrawPrefabListSection();
        EditorGUILayout.Space();

        DrawActionButtons();

        if (isProcessing)
        {
            EditorGUILayout.HelpBox("처리 중입니다. 잠시만 기다려주세요...", MessageType.Info);
        }
    }

    private void DrawSettingsSection()
    {
        GUILayout.Label("설정", EditorStyles.boldLabel);

        targetProp = (GameObject)EditorGUILayout.ObjectField(
            "타겟 Prop (씬 오브젝트)",
            targetProp,
            typeof(GameObject),
            true
        );

        EditorGUILayout.Space();

        outputFolder = EditorGUILayout.TextField("출력 폴더", outputFolder);
        thumbnailResolution = EditorGUILayout.IntSlider("해상도", thumbnailResolution, 128, 2048);

        EditorGUILayout.Space();
        GUILayout.Label("크기 조정 설정", EditorStyles.boldLabel);

        autoScale = EditorGUILayout.Toggle("자동 크기 조정", autoScale);

        if (autoScale)
        {
            maxSize = EditorGUILayout.Slider("최대 크기 (유니티 단위)", maxSize, 0.5f, 10f);
            minSize = EditorGUILayout.Slider("최소 크기 (유니티 단위)", minSize, 0.1f, 5f);
        }

        EditorGUILayout.Space();
        GUILayout.Label("카메라 설정", EditorStyles.boldLabel);

        cameraDistance = EditorGUILayout.Slider("카메라 거리", cameraDistance, 2f, 20f);
        cameraAngle = EditorGUILayout.Vector3Field("카메라 각도", cameraAngle);
        backgroundColor = EditorGUILayout.ColorField("배경색 (투명화)", backgroundColor);
    }

    private void DrawPrefabListSection()
    {
        GUILayout.Label("프리팹 목록", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("프리팹 추가", GUILayout.Width(100)))
        {
            prefabsToProcess.Add(null);
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

        EditorGUILayout.HelpBox(
            $"등록된 프리팹: {prefabsToProcess.Count}개",
            MessageType.Info
        );
    }

    private void DrawActionButtons()
    {
        EditorGUI.BeginDisabledGroup(isProcessing || prefabsToProcess.Count == 0);

        if (GUILayout.Button("썸네일 생성", GUILayout.Height(40)))
        {
            GenerateThumbnails();
        }

        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space();

        if (GUILayout.Button("선택된 프리팹 일괄 추가"))
        {
            AddSelectedPrefabs();
        }
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

        try
        {
            SetupRenderCamera();

            int successCount = 0;
            int totalCount = prefabsToProcess.Count;

            for (int i = 0; i < totalCount; i++)
            {
                GameObject prefab = prefabsToProcess[i];

                if (prefab == null)
                {
                    Debug.LogWarning($"프리팹 {i + 1}이(가) null입니다. 건너뜁니다.");
                    continue;
                }

                float progress = (float)i / totalCount;
                EditorUtility.DisplayProgressBar(
                    "썸네일 생성 중",
                    $"처리 중: {prefab.name} ({i + 1}/{totalCount})",
                    progress
                );

                if (GenerateSingleThumbnail(prefab))
                {
                    successCount++;
                }
            }

            EditorUtility.ClearProgressBar();

            EditorUtility.DisplayDialog(
                "완료",
                $"썸네일 생성 완료!\n성공: {successCount}/{totalCount}\n저장 위치: {outputFolder}",
                "확인"
            );

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
        }
    }

    private bool GenerateSingleThumbnail(GameObject prefab)
    {
        try
        {
            // 1. 프리팹을 타겟 Prop의 자식으로 인스턴스화
            GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            instance.transform.SetParent(targetProp.transform);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            // 2. 크기 정규화
            if (autoScale)
            {
                NormalizeObjectSize(instance);
            }

            // 3. 카메라 위치 조정
            PositionCamera(instance);

            // 4. 스크린샷 촬영
            Texture2D screenshot = CaptureScreenshot();

            // 5. 마젠타 컬러를 투명으로 변환
            Texture2D transparentTexture = ConvertMagentaToTransparent(screenshot);

            // 6. PNG로 저장
            string fileName = $"{prefab.name}_thumbnail.png";
            string filePath = Path.Combine(outputFolder, fileName);
            SaveTextureToPNG(transparentTexture, filePath);

            // 7. 정리
            DestroyImmediate(instance);
            DestroyImmediate(screenshot);
            DestroyImmediate(transparentTexture);

            Debug.Log($"썸네일 생성 완료: {fileName}");
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"프리팹 '{prefab.name}' 처리 중 오류 발생: {e.Message}");
            return false;
        }
    }

    #endregion

    #region Size Normalization

    /// <summary>
    /// 오브젝트 크기를 정규화
    /// - 최대 크기를 넘지 않도록 축소
    /// - 너무 작으면 최소 크기로 확대
    /// </summary>
    private void NormalizeObjectSize(GameObject obj)
    {
        Bounds bounds = CalculateBounds(obj);

        if (bounds.size == Vector3.zero)
        {
            Debug.LogWarning($"오브젝트 '{obj.name}'의 크기를 계산할 수 없습니다.");
            return;
        }

        // 가장 큰 축의 크기 찾기
        float maxDimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);

        float scaleFactor = 1f;

        // 최대 크기 제한 (2를 넘지 않도록)
        if (maxDimension > maxSize)
        {
            scaleFactor = maxSize / maxDimension;
            Debug.Log($"오브젝트 '{obj.name}' 크기 축소: {maxDimension:F2} -> {maxSize:F2} (scale: {scaleFactor:F2})");
        }
        // 최소 크기 제한 (너무 작으면 1x1로)
        else if (maxDimension < minSize)
        {
            scaleFactor = minSize / maxDimension;
            Debug.Log($"오브젝트 '{obj.name}' 크기 확대: {maxDimension:F2} -> {minSize:F2} (scale: {scaleFactor:F2})");
        }

        if (scaleFactor != 1f)
        {
            obj.transform.localScale = Vector3.one * scaleFactor;
        }
    }

    /// <summary>
    /// 오브젝트의 전체 바운드 계산 (모든 자식 포함)
    /// </summary>
    private Bounds CalculateBounds(GameObject obj)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();

        if (renderers.Length == 0)
        {
            return new Bounds(obj.transform.position, Vector3.zero);
        }

        Bounds bounds = renderers[0].bounds;

        foreach (Renderer renderer in renderers)
        {
            bounds.Encapsulate(renderer.bounds);
        }

        return bounds;
    }

    #endregion

    #region Camera Setup

    private void SetupRenderCamera()
    {
        GameObject cameraObj = new GameObject("ThumbnailCamera");
        renderCamera = cameraObj.AddComponent<Camera>();

        renderCamera.clearFlags = CameraClearFlags.SolidColor;
        renderCamera.backgroundColor = backgroundColor;
        renderCamera.orthographic = false;
        renderCamera.fieldOfView = 30f;
        renderCamera.nearClipPlane = 0.1f;
        renderCamera.farClipPlane = 100f;
    }

    private void PositionCamera(GameObject target)
    {
        Bounds bounds = CalculateBounds(target);
        Vector3 center = bounds.center;

        // 카메라를 타겟 중심에서 일정 거리만큼 떨어뜨리기
        renderCamera.transform.position = center + Quaternion.Euler(cameraAngle) * Vector3.back * cameraDistance;
        renderCamera.transform.LookAt(center);

        // 오브젝트가 화면에 잘 들어오도록 FOV 조정
        float maxDimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        float fov = 2f * Mathf.Atan(maxDimension / (2f * cameraDistance)) * Mathf.Rad2Deg;
        renderCamera.fieldOfView = Mathf.Clamp(fov * 1.2f, 15f, 60f); // 여유 공간 20% 추가
    }

    private void CleanupRenderCamera()
    {
        if (renderCamera != null)
        {
            DestroyImmediate(renderCamera.gameObject);
            renderCamera = null;
        }
    }

    #endregion

    #region Screenshot & Image Processing

    private Texture2D CaptureScreenshot()
    {
        RenderTexture rt = new RenderTexture(thumbnailResolution, thumbnailResolution, 24);
        renderCamera.targetTexture = rt;

        Texture2D screenshot = new Texture2D(thumbnailResolution, thumbnailResolution, TextureFormat.RGBA32, false);

        renderCamera.Render();

        RenderTexture.active = rt;
        screenshot.ReadPixels(new Rect(0, 0, thumbnailResolution, thumbnailResolution), 0, 0);
        screenshot.Apply();

        renderCamera.targetTexture = null;
        RenderTexture.active = null;
        DestroyImmediate(rt);

        return screenshot;
    }

    /// <summary>
    /// 마젠타 컬러를 투명으로 변환
    /// 색상 유사도를 사용하여 마젠타와 비슷한 색도 함께 처리
    /// </summary>
    private Texture2D ConvertMagentaToTransparent(Texture2D source)
    {
        Texture2D result = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        Color[] pixels = source.GetPixels();

        // 마젠타 색상 (RGB: 1, 0, 1)
        Color magenta = backgroundColor;
        float threshold = 0.1f; // 색상 유사도 임계값

        for (int i = 0; i < pixels.Length; i++)
        {
            Color pixel = pixels[i];

            // 마젠타와의 색상 거리 계산
            float distance = Mathf.Sqrt(
                Mathf.Pow(pixel.r - magenta.r, 2) +
                Mathf.Pow(pixel.g - magenta.g, 2) +
                Mathf.Pow(pixel.b - magenta.b, 2)
            );

            // 마젠타와 유사한 색상이면 투명하게
            if (distance < threshold)
            {
                pixels[i] = new Color(pixel.r, pixel.g, pixel.b, 0f);
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

                // 프리팹인지 확인
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
