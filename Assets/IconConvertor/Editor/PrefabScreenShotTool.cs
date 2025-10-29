using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

public class PrefabScreenshotTool : EditorWindow
{
    [MenuItem("Tools/Prefab Screenshot Tool")]
    public static void ShowWindow()
    {
        GetWindow<PrefabScreenshotTool>("Prefab Screenshot");
    }

    private List<GameObject> prefabList = new List<GameObject>();
    private GameObject probParent;
    private Camera screenshotCamera;

    // 월드 공간 기준 크기 제한
    private float minWorldSize = 1f;
    private float maxWorldSize = 2f;

    private int screenshotWidth = 512;
    private int screenshotHeight = 512;
    private string savePath = "Assets/Screenshots";

    private Vector2 scrollPosition;

    private void OnGUI()
    {
        GUILayout.Label("Prefab Screenshot Tool", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // Prob 부모 오브젝트 참조
        probParent = (GameObject)EditorGUILayout.ObjectField(
            "Prob Parent",
            probParent,
            typeof(GameObject),
            true
        );

        // 카메라 참조
        screenshotCamera = (Camera)EditorGUILayout.ObjectField(
            "Screenshot Camera",
            screenshotCamera,
            typeof(Camera),
            true
        );

        EditorGUILayout.Space();

        // 월드 크기 설정
        GUILayout.Label("World Size Settings (Unity Units)", EditorStyles.boldLabel);
        minWorldSize = EditorGUILayout.FloatField("Min World Size", minWorldSize);
        maxWorldSize = EditorGUILayout.FloatField("Max World Size", maxWorldSize);

        EditorGUILayout.HelpBox(
            "Objects smaller than Min Size will be scaled up to Min Size.\n" +
            "Objects between Min and Max will keep their original size.\n" +
            "Objects larger than Max Size will be scaled down to Max Size.",
            MessageType.Info
        );

        EditorGUILayout.Space();

        // 스크린샷 설정
        GUILayout.Label("Screenshot Settings", EditorStyles.boldLabel);
        screenshotWidth = EditorGUILayout.IntField("Width", screenshotWidth);
        screenshotHeight = EditorGUILayout.IntField("Height", screenshotHeight);

        EditorGUILayout.BeginHorizontal();
        savePath = EditorGUILayout.TextField("Save Path", savePath);
        if (GUILayout.Button("Browse", GUILayout.Width(60)))
        {
            string path = EditorUtility.OpenFolderPanel("Select Save Folder", "Assets", "");
            if (!string.IsNullOrEmpty(path))
            {
                savePath = "Assets" + path.Substring(Application.dataPath.Length);
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        // 드래그 앤 드롭 영역
        GUILayout.Label("Drag & Drop Prefabs Here", EditorStyles.boldLabel);

        Rect dropArea = GUILayoutUtility.GetRect(0f, 100f, GUILayout.ExpandWidth(true));
        GUI.Box(dropArea, "Drop Prefabs Here");

        HandleDragAndDrop(dropArea);

        EditorGUILayout.Space();

        // 프리팹 리스트
        GUILayout.Label($"Prefab List ({prefabList.Count})", EditorStyles.boldLabel);

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(200));

        for (int i = prefabList.Count - 1; i >= 0; i--)
        {
            EditorGUILayout.BeginHorizontal();
            prefabList[i] = (GameObject)EditorGUILayout.ObjectField(
                prefabList[i],
                typeof(GameObject),
                false
            );

            if (GUILayout.Button("Remove", GUILayout.Width(60)))
            {
                prefabList.RemoveAt(i);
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();

        // 실행 버튼들
        EditorGUILayout.BeginHorizontal();

        GUI.enabled = prefabList.Count > 0 && probParent != null && screenshotCamera != null;
        if (GUILayout.Button("Take All Screenshots", GUILayout.Height(30)))
        {
            TakeAllScreenshots();
        }
        GUI.enabled = true;

        if (GUILayout.Button("Clear List", GUILayout.Height(30)))
        {
            prefabList.Clear();
        }

        EditorGUILayout.EndHorizontal();
    }

    private void HandleDragAndDrop(Rect dropArea)
    {
        Event evt = Event.current;

        if (!dropArea.Contains(evt.mousePosition))
            return;

        switch (evt.type)
        {
            case EventType.DragUpdated:
            case EventType.DragPerform:
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();

                    foreach (Object draggedObject in DragAndDrop.objectReferences)
                    {
                        GameObject prefab = draggedObject as GameObject;
                        if (prefab != null && !prefabList.Contains(prefab))
                        {
                            prefabList.Add(prefab);
                        }
                    }
                }
                Event.current.Use();
                break;
        }
    }

    private void TakeAllScreenshots()
    {
        if (!Directory.Exists(savePath))
        {
            Directory.CreateDirectory(savePath);
        }

        for (int i = 0; i < prefabList.Count; i++)
        {
            GameObject prefab = prefabList[i];

            EditorUtility.DisplayProgressBar(
                "Taking Screenshots",
                $"Processing {prefab.name} ({i + 1}/{prefabList.Count})",
                (float)i / prefabList.Count
            );

            TakeScreenshot(prefab);
        }

        EditorUtility.ClearProgressBar();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "Complete",
            $"Successfully captured {prefabList.Count} screenshots!\nSaved to: {savePath}",
            "OK"
        );
    }

    private void TakeScreenshot(GameObject prefab)
    {
        // Prob 하위에 프리팹 인스턴스 생성
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, probParent.transform);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;

        // 월드 크기 기준으로 스케일 조정
        AdjustScaleByWorldSize(instance);

        // 한 프레임 대기 (렌더링 업데이트)
        EditorApplication.QueuePlayerLoopUpdate();
        SceneView.RepaintAll();

        // 스크린샷 캡처
        RenderTexture rt = new RenderTexture(screenshotWidth, screenshotHeight, 24);
        screenshotCamera.targetTexture = rt;
        screenshotCamera.Render();

        RenderTexture.active = rt;
        Texture2D screenshot = new Texture2D(screenshotWidth, screenshotHeight, TextureFormat.RGBA32, false);
        screenshot.ReadPixels(new Rect(0, 0, screenshotWidth, screenshotHeight), 0, 0);
        screenshot.Apply();

        // 파일 저장
        byte[] bytes = screenshot.EncodeToPNG();
        string filename = $"{savePath}/{prefab.name}.png";
        File.WriteAllBytes(filename, bytes);

        // 정리
        screenshotCamera.targetTexture = null;
        RenderTexture.active = null;
        DestroyImmediate(rt);
        DestroyImmediate(screenshot);
        DestroyImmediate(instance);
    }

    private void AdjustScaleByWorldSize(GameObject obj)
    {
        // 모든 Renderer를 포함한 전체 Bounds 계산
        Bounds bounds = CalculateTotalBounds(obj);

        if (bounds.size == Vector3.zero)
        {
            Debug.LogWarning($"{obj.name}에 Renderer가 없습니다. 스케일 조정을 건너뜁니다.");
            return;
        }

        // XZ 평면 기준 최대 크기 (위에서 내려다본 크기)
        float maxDimension = Mathf.Max(bounds.size.x, bounds.size.z);

        float scaleFactor = 1f;

        if (maxDimension > maxWorldSize)
        {
            // 2 유니티 단위보다 크면 2로 축소
            scaleFactor = maxWorldSize / maxDimension;
            Debug.Log($"{obj.name}: 크기 {maxDimension:F2} → {maxWorldSize} 유니티 단위로 축소 (scale factor: {scaleFactor:F2})");
        }
        else if (maxDimension < minWorldSize)
        {
            // 1 유니티 단위보다 작으면 1로 확대
            scaleFactor = minWorldSize / maxDimension;
            Debug.Log($"{obj.name}: 크기 {maxDimension:F2} → {minWorldSize} 유니티 단위로 확대 (scale factor: {scaleFactor:F2})");
        }
        else
        {
            // 1~2 사이면 원본 크기 유지
            Debug.Log($"{obj.name}: 크기 {maxDimension:F2} - 원본 크기 유지");
        }

        obj.transform.localScale = obj.transform.localScale * scaleFactor;
    }

    private Bounds CalculateTotalBounds(GameObject obj)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();

        if (renderers.Length == 0)
            return new Bounds(obj.transform.position, Vector3.zero);

        Bounds bounds = renderers[0].bounds;

        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        return bounds;
    }
}