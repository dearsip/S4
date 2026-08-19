using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace S4Viewer
{
    /// <summary>
    /// A self-contained S4 / tetrahedral symmetry viewer.  The scene is built at
    /// runtime so the sample scene works without prefab or package dependencies.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class S4TetrahedronApp : MonoBehaviour
    {
        [Header("Geometry")]
        [SerializeField, Min(0.02f)] private float vertexRadius = 0.13f;
        [SerializeField, Min(0.01f)] private float edgeRadius = 0.035f;
        [SerializeField] private Color tetrahedronColor = new Color(0.27f, 0.72f, 1f, 1f);
        [SerializeField] private Color fundamentalRegionColor = new Color(0.95f, 0.08f, 0.08f, 0.82f);
        [SerializeField] private bool showOriginalPose = true;
        [SerializeField, Min(0.02f)] private float fixedLabelSize = 0.13f;
        [SerializeField, Min(0.02f)] private float movingLabelSize = 0.22f;
        [SerializeField] private Vector2 fixedLabelOffset = new Vector2(-0.18f, 0.18f);
        [SerializeField] private Vector2 movingLabelOffset = new Vector2(0.22f, 0.22f);

        [Header("Lambert material")]
        [SerializeField, Range(0f, 2f)] private float lambertBrightness = 1f;
        [SerializeField, Range(0f, 2f)] private float lambertDiffuseStrength = 1f;
        [SerializeField, Range(0f, 1f)] private float lambertConstantLight;

        [Header("Animation")]
        [SerializeField, Min(0.05f)] private float animationDuration = 1.1f;
        [SerializeField] private bool showAnimationGuides = true;
        [SerializeField] private Color rotationAxisColor = new Color(1f, 0.72f, 0.08f, 0.95f);
        [SerializeField] private Color reflectionPlaneColor = new Color(0.08f, 0.78f, 1f, 0.2f);
        [SerializeField, Min(0.5f)] private float guideExtent = 2.5f;

        [Header("Conjugacy class colors")]
        [SerializeField] private Color identityColor = new Color(0.93f, 0.93f, 0.93f, 0.72f);
        [SerializeField] private Color transpositionColor = new Color(1f, 0.56f, 0.12f, 0.68f);
        [SerializeField] private Color doubleTranspositionColor = new Color(0.26f, 0.84f, 0.40f, 0.68f);
        [SerializeField] private Color threeCycleColor = new Color(0.23f, 0.56f, 1f, 0.68f);
        [SerializeField] private Color fourCycleColor = new Color(0.78f, 0.28f, 1f, 0.68f);

        [Header("Camera")]
        [SerializeField] private float autoRotateDegreesPerSecond = 8f;

        [Header("Group mode")]
        [SerializeField] private GroupMode groupMode = GroupMode.S4;
        [SerializeField] private bool multiplyOnRight;

        private static readonly Vector3[] Vertices =
        {
            new Vector3(1, 1, 1),
            new Vector3(1, -1, -1),
            new Vector3(-1, 1, -1),
            new Vector3(-1, -1, 1)
        };

        // vertex, incident edge centre, incident face centre
        private static readonly Vector3[] FundamentalTriangle =
        {
            new Vector3(1, 1, 1),
            new Vector3(1, 0, 0),
            new Vector3(1f / 3f, 1f / 3f, -1f / 3f)
        };

        private readonly List<Transform> activeVertices = new List<Transform>();
        private readonly List<Transform> activeEdges = new List<Transform>();
        private readonly List<GameObject> conjugacyGroups = new List<GameObject>();
        private readonly List<GameObject> activeRegionObjects = new List<GameObject>();
        private readonly List<GameObject> originalRegionObjects = new List<GameObject>();
        private readonly List<Mesh> activeRegionMeshes = new List<Mesh>();
        private readonly List<Permutation> modeChambers = new List<Permutation>();
        private readonly List<KeyValuePair<Permutation, Button>> permutationButtons = new List<KeyValuePair<Permutation, Button>>();
        private Transform activeRoot;
        private Transform originalRoot;
        private Transform overlayRoot;
        private Transform animationGuideRoot;
        private Transform rotationAxisGuide;
        private Transform reflectionPlaneGuide;
        private Mesh reflectionPlaneMesh;
        private bool guideAnimationInProgress;
        private Transform conjugacyToggleContainer;
        private Transform individualToggleContainer;
        private Transform scrollContent;
        private readonly List<GameObject> individualRegionGroups = new List<GameObject>();
        private readonly List<Mesh> individualMovingRegionMeshes = new List<Mesh>();
        private readonly List<Permutation> individualRepresentatives = new List<Permutation>();
        private readonly List<Matrix4x4> individualBlockMatrices = new List<Matrix4x4>();
        private readonly List<int> individualMeshBlockIndices = new List<int>();
        private readonly List<Permutation> individualMeshChambers = new List<Permutation>();
        private readonly List<Material> lambertMaterials = new List<Material>();
        private OrbitCamera orbitCamera;
        private Text statusText;
        private Text poseText;
        private Text speedText;
        private InputField cameraStateField;
        private Slider autoRotationSlider;
        private Toggle autoRotationToggle;
        private Button multiplySideButton;
        private Toggle originalToggle;
        private Coroutine animationRoutine;
        private readonly Queue<Permutation> pendingPermutations = new Queue<Permutation>();
        private Permutation currentPermutation = Permutation.Identity;
        private Matrix4x4 currentMatrix = Matrix4x4.identity;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void S4ClipboardWrite(string text);

        [DllImport("__Internal")]
        private static extern void S4ClipboardRead(string receiverName);

        [DllImport("__Internal")]
        private static extern void S4ClipboardInstallPasteHandler(string receiverName);
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureAppExists()
        {
            if (FindObjectOfType<S4TetrahedronApp>() == null)
                new GameObject("S4 Tetrahedron App").AddComponent<S4TetrahedronApp>();
        }

        private void Awake()
        {
            QualitySettings.antiAliasing = 4;
            BuildWorld();
            BuildCamera();
            BuildUi();
#if UNITY_WEBGL && !UNITY_EDITOR
            S4ClipboardInstallPasteHandler(gameObject.name);
#endif
            ApplyMatrix(Matrix4x4.identity);
            statusText.text = "current: e\nidentity";
        }

        private void OnValidate()
        {
            for (int i = lambertMaterials.Count - 1; i >= 0; i--)
            {
                Material material = lambertMaterials[i];
                if (material == null)
                {
                    lambertMaterials.RemoveAt(i);
                    continue;
                }
                ApplyLambertSettings(material);
            }
        }

        private void BuildWorld()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.56f, 0.58f, 0.64f);

            activeRoot = new GameObject("Selected permutation").transform;
            activeRoot.SetParent(transform, false);
            BuildTetrahedron(activeRoot, tetrahedronColor, activeVertices, activeEdges, true);

            originalRoot = new GameObject("Original pose (reference)").transform;
            originalRoot.SetParent(transform, false);
            var ghost = new Color(0.72f, 0.78f, 0.86f, 0.23f);
            BuildTetrahedron(originalRoot, ghost, null, null, false);
            originalRoot.gameObject.SetActive(showOriginalPose);

            CreateVertexLabels();
            overlayRoot = new GameObject("Region overlays").transform;
            overlayRoot.SetParent(transform, false);
            BuildAnimationGuides();
            RebuildModeGeometry();

            if (FindObjectsOfType<Light>().Length == 0)
            {
                var lightObject = new GameObject("Key light");
                lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.15f;
            }
        }

        private void BuildTetrahedron(Transform parent, Color color, List<Transform> vertexList,
            List<Transform> edgeList, bool castShadows)
        {
            Material material = CreateMaterial(color);
            for (int i = 0; i < Vertices.Length; i++)
            {
                GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = "Vertex " + (i + 1);
                sphere.transform.SetParent(parent, false);
                sphere.transform.localPosition = Vertices[i];
                sphere.transform.localScale = Vector3.one * (2f * vertexRadius);
                sphere.GetComponent<Renderer>().sharedMaterial = material;
                sphere.GetComponent<Renderer>().shadowCastingMode = castShadows
                    ? UnityEngine.Rendering.ShadowCastingMode.On
                    : UnityEngine.Rendering.ShadowCastingMode.Off;
                Destroy(sphere.GetComponent<Collider>());
                vertexList?.Add(sphere.transform);
            }

            for (int a = 0; a < 4; a++)
            for (int b = a + 1; b < 4; b++)
            {
                GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                cylinder.name = "Edge " + (a + 1) + "-" + (b + 1);
                cylinder.transform.SetParent(parent, false);
                cylinder.GetComponent<Renderer>().sharedMaterial = material;
                cylinder.GetComponent<Renderer>().shadowCastingMode = castShadows
                    ? UnityEngine.Rendering.ShadowCastingMode.On
                    : UnityEngine.Rendering.ShadowCastingMode.Off;
                Destroy(cylinder.GetComponent<Collider>());
                SetCylinder(cylinder.transform, Vertices[a], Vertices[b], edgeRadius);
                edgeList?.Add(cylinder.transform);
            }
        }

        private void CreateVertexLabels()
        {
            for (int i = 0; i < 4; i++)
            {
                CreateVertexLabel("Fixed label " + (i + 1), originalRoot, Vertices[i], i + 1,
                    fixedLabelSize, fixedLabelOffset);
                CreateVertexLabel("Moving label " + (i + 1), activeRoot, Vertices[i], i + 1,
                    movingLabelSize, movingLabelOffset, activeVertices[i]);
            }
        }

        private static void CreateVertexLabel(string name, Transform parent, Vector3 position, int number,
            float size, Vector2 screenOffset, Transform follow = null)
        {
            GameObject go = new GameObject(name, typeof(TextMesh), typeof(WorldVertexLabel));
            go.transform.SetParent(parent, false);
            TextMesh text = go.GetComponent<TextMesh>();
            text.text = number.ToString();
            text.fontSize = 640;
            text.characterSize = size * .1f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = new Color(0.06f, 0.08f, 0.12f, 1f);
            text.GetComponent<MeshRenderer>().sortingOrder = 20;
            go.GetComponent<WorldVertexLabel>().Configure(follow, position, screenOffset);
        }

        private void BuildAnimationGuides()
        {
            animationGuideRoot = new GameObject("Animation guides").transform;
            animationGuideRoot.SetParent(transform, false);

            GameObject axis = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            axis.name = "Rotation axis";
            axis.transform.SetParent(animationGuideRoot, false);
            Destroy(axis.GetComponent<Collider>());
            axis.GetComponent<Renderer>().sharedMaterial = CreateMaterial(rotationAxisColor);
            axis.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rotationAxisGuide = axis.transform;

            GameObject plane = new GameObject("Reflection plane", typeof(MeshFilter), typeof(MeshRenderer));
            plane.transform.SetParent(animationGuideRoot, false);
            reflectionPlaneGuide = plane.transform;
            reflectionPlaneMesh = new Mesh { name = "Reflection plane mesh" };
            plane.GetComponent<MeshFilter>().sharedMesh = reflectionPlaneMesh;
            plane.GetComponent<MeshRenderer>().sharedMaterial = CreateFlatMaterial(reflectionPlaneColor);
            plane.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            animationGuideRoot.gameObject.SetActive(false);
        }

        private void ShowAnimationGuides(Permutation operation, Pose delta, Matrix4x4 start)
        {
            bool showPlane = !operation.IsEven && operation.Type != CycleType.Identity;
            bool showAxis = operation.IsEven && operation.Type != CycleType.Identity ||
                            operation.Type == CycleType.FourCycle;
            guideAnimationInProgress = showPlane || showAxis;

            rotationAxisGuide.gameObject.SetActive(showAxis);
            reflectionPlaneGuide.gameObject.SetActive(showPlane);
            if (showAxis)
            {
                Vector3 axis = multiplyOnRight ? start.MultiplyVector(delta.RotationAxis).normalized : delta.RotationAxis;
                SetCylinder(rotationAxisGuide, -axis * guideExtent, axis * guideExtent, 0.022f);
            }
            if (showPlane)
            {
                Vector3 normal = multiplyOnRight
                    ? start.MultiplyVector(delta.ReflectionNormal).normalized
                    : delta.ReflectionNormal;
                SetReflectionPlane(normal);
            }
            animationGuideRoot.gameObject.SetActive(showAnimationGuides && guideAnimationInProgress);
        }

        private void HideAnimationGuides()
        {
            guideAnimationInProgress = false;
            if (animationGuideRoot != null) animationGuideRoot.gameObject.SetActive(false);
        }

        private void SetReflectionPlane(Vector3 normal)
        {
            normal.Normalize();
            Vector3 tangent = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 bitangent = Vector3.Cross(normal, tangent).normalized;
            Vector3 a = (-tangent - bitangent) * guideExtent;
            Vector3 b = ( tangent - bitangent) * guideExtent;
            Vector3 c = ( tangent + bitangent) * guideExtent;
            Vector3 d = (-tangent + bitangent) * guideExtent;
            reflectionPlaneMesh.Clear();
            reflectionPlaneMesh.vertices = new[] { a, b, c, d, a, d, c, b };
            reflectionPlaneMesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 };
            reflectionPlaneMesh.RecalculateNormals();
            reflectionPlaneMesh.RecalculateBounds();
        }

        private void RebuildModeGeometry()
        {
            foreach (GameObject go in activeRegionObjects) Destroy(go);
            foreach (GameObject go in originalRegionObjects) Destroy(go);
            activeRegionObjects.Clear();
            originalRegionObjects.Clear();
            activeRegionMeshes.Clear();
            modeChambers.Clear();
            modeChambers.AddRange(GetModeChambers());

            for (int i = 0; i < modeChambers.Count; i++)
            {
                bool primary = i == 0;
                Color activeColor = primary ? fundamentalRegionColor : Darkened(fundamentalRegionColor, 0.48f);
                Color originalColor = activeColor;
                originalColor.a *= 0.28f;
                Vector3[] chamber = FundamentalTriangle.Select(modeChambers[i].Transform).ToArray();

                Mesh activeMesh = CreateTriangle("Moving chamber " + i, activeRoot, activeColor, chamber);
                activeRegionMeshes.Add(activeMesh);
                activeRegionObjects.Add(activeRoot.GetChild(activeRoot.childCount - 1).gameObject);
                Mesh originalMesh = CreateTriangle("Fixed chamber " + i, originalRoot, originalColor, chamber);
                originalRegionObjects.Add(originalRoot.GetChild(originalRoot.childCount - 1).gameObject);
            }

            ApplyMatrix(currentMatrix);
            BuildIndividualRegionOverlays();
            RebuildConjugacyOverlays();
        }

        private void BuildCamera()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                cam = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }

            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.white;//new Color(0.045f, 0.055f, 0.075f);
            cam.nearClipPlane = 0.05f;
            orbitCamera = cam.GetComponent<OrbitCamera>() ?? cam.gameObject.AddComponent<OrbitCamera>();
            orbitCamera.Configure(Vector3.zero, 15f, 23f, -35f, autoRotateDegreesPerSecond);
        }

        private void BuildUi()
        {
            if (FindObjectOfType<EventSystem>() == null)
            {
                var eventObject = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                eventObject.transform.SetParent(transform, false);
            }

            var canvasObject = new GameObject("S4 controls", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;

            poseText = AddText(canvasObject.transform, "e", 42, TextAnchor.MiddleCenter);
            poseText.color = new Color(0.05f, 0.07f, 0.11f, 1f);
            RectTransform poseRect = poseText.rectTransform;
            poseRect.anchorMin = poseRect.anchorMax = new Vector2(0.5f, 0f);
            poseRect.pivot = new Vector2(0.5f, 0f);
            poseRect.anchoredPosition = new Vector2(0f, 18f);
            poseRect.sizeDelta = new Vector2(720f, 48f);

            GameObject panel = UiObject("Control panel", canvasObject.transform, typeof(Image), typeof(ScrollRect));
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 0f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 0.5f);
            panelRect.sizeDelta = new Vector2(350f, 0f);
            panel.GetComponent<Image>().color = new Color(0.04f, 0.055f, 0.08f, 0.94f);

            ResponsiveMenu responsiveMenu = canvasObject.AddComponent<ResponsiveMenu>();
            Button menuToggle = AddButton(canvasObject.transform, "<<", responsiveMenu.Toggle);
            SetRect(menuToggle.GetComponent<RectTransform>(), new Vector2(304, -8), new Vector2(40, 34), new Vector2(0, 1));
            responsiveMenu.Configure(panelRect, menuToggle, scaler);

            GameObject viewport = UiObject("Viewport", panel.transform, typeof(Image), typeof(Mask));
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = new Vector2(-18f, 0f);
            viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.005f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            GameObject content = UiObject("Content", viewport.transform);
            scrollContent = content.transform;
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0f, 1120f);

            Scrollbar scrollbar = AddVerticalScrollbar(panel.transform);
            RectTransform scrollbarRect = scrollbar.GetComponent<RectTransform>();
            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = Vector2.one;
            scrollbarRect.pivot = Vector2.one;
            scrollbarRect.anchoredPosition = Vector2.zero;
            scrollbarRect.sizeDelta = new Vector2(18f, 0f);

            ScrollRect scrollRect = panel.GetComponent<ScrollRect>();
            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            scrollRect.scrollSensitivity = 32f;

            Transform uiParent = scrollContent;

            Text title = AddText(uiParent, "S4  /  Tetrahedral symmetry", 20, TextAnchor.MiddleCenter);
            SetRect(title.rectTransform, new Vector2(12, -12), new Vector2(306, 32), new Vector2(0, 1));

            statusText = AddText(uiParent, "", 16, TextAnchor.MiddleCenter);
            SetRect(statusText.rectTransform, new Vector2(12, -49), new Vector2(306, 48), new Vector2(0, 1));

            int index = 0;
            foreach (Permutation p in Permutation.All())
            {
                int column = index % 4;
                int row = index / 4;
                Button button = AddButton(uiParent, p.CycleNotation, () => SelectPermutation(p));
                SetRect(button.GetComponent<RectTransform>(), new Vector2(12 + column * 77, -102 - row * 43),
                    new Vector2(72, 36), new Vector2(0, 1));
                ColorBlock colors = button.colors;
                colors.normalColor = ClassColor(p.Type) * new Color(0.72f, 0.72f, 0.72f, 1f);
                colors.highlightedColor = ClassColor(p.Type);
                button.colors = colors;
                permutationButtons.Add(new KeyValuePair<Permutation, Button>(p, button));
                index++;
            }

            Text modeTitle = AddText(uiParent, "Group mode", 16, TextAnchor.MiddleLeft);
            SetRect(modeTitle.rectTransform, new Vector2(16, -370), new Vector2(110, 26), new Vector2(0, 1));
            Button s4Mode = AddButton(uiParent, "S4", () => SetGroupMode(GroupMode.S4));
            Button a4Mode = AddButton(uiParent, "A4", () => SetGroupMode(GroupMode.A4));
            Button v4Mode = AddButton(uiParent, "V4", () => SetGroupMode(GroupMode.V4));
            SetRect(s4Mode.GetComponent<RectTransform>(), new Vector2(126, -368), new Vector2(58, 28), new Vector2(0, 1));
            SetRect(a4Mode.GetComponent<RectTransform>(), new Vector2(190, -368), new Vector2(58, 28), new Vector2(0, 1));
            SetRect(v4Mode.GetComponent<RectTransform>(), new Vector2(254, -368), new Vector2(58, 28), new Vector2(0, 1));

            multiplySideButton = AddButton(uiParent, "", ToggleMultiplicationSide);
            SetRect(multiplySideButton.GetComponent<RectTransform>(), new Vector2(16, -404), new Vector2(170, 28), new Vector2(0, 1));
            UpdateMultiplicationSideButton();

            Text classTitle = AddText(uiParent, "Conjugacy class overlays", 16, TextAnchor.MiddleLeft);
            SetRect(classTitle.rectTransform, new Vector2(16, -442), new Vector2(300, 26), new Vector2(0, 1));

            GameObject classContainer = UiObject("Conjugacy toggles", uiParent);
            conjugacyToggleContainer = classContainer.transform;
            SetRect(classContainer.GetComponent<RectTransform>(), new Vector2(16, -472), new Vector2(300, 124), new Vector2(0, 1));

            Text regionTitle = AddText(uiParent, "Individual region visibility", 16, TextAnchor.MiddleLeft);
            SetRect(regionTitle.rectTransform, new Vector2(16, -604), new Vector2(300, 24), new Vector2(0, 1));
            GameObject individualContainer = UiObject("Individual region toggles", uiParent);
            individualToggleContainer = individualContainer.transform;
            SetRect(individualContainer.GetComponent<RectTransform>(), new Vector2(16, -634),
                new Vector2(304, 156), new Vector2(0, 1));

            originalToggle = AddToggle(uiParent, "Keep original pose", fundamentalRegionColor, value =>
            {
                showOriginalPose = value;
                originalRoot.gameObject.SetActive(value);
            });
            SetRect(originalToggle.GetComponent<RectTransform>(), new Vector2(16, -798), new Vector2(300, 28), new Vector2(0, 1));
            originalToggle.isOn = showOriginalPose;

            speedText = AddText(uiParent, "Auto rotation: " + autoRotateDegreesPerSecond.ToString("0.0") + " deg/s",
                14, TextAnchor.MiddleLeft);
            SetRect(speedText.rectTransform, new Vector2(16, -834), new Vector2(300, 22), new Vector2(0, 1));
            autoRotationSlider = AddSlider(uiParent, -30f, 30f, autoRotateDegreesPerSecond, value =>
            {
                autoRotateDegreesPerSecond = value;
                orbitCamera.AutoRotateSpeed = value;
                speedText.text = "Auto rotation: " + value.ToString("0.0") + " deg/s";
            });
            SetRect(autoRotationSlider.GetComponent<RectTransform>(), new Vector2(16, -859), new Vector2(300, 22), new Vector2(0, 1));

            autoRotationToggle = AddToggle(uiParent, "Auto rotation enabled", new Color(0.25f, 0.68f, 1f),
                value => orbitCamera.AutoRotateEnabled = value);
            SetRect(autoRotationToggle.GetComponent<RectTransform>(), new Vector2(16, -889), new Vector2(300, 27), new Vector2(0, 1));
            autoRotationToggle.isOn = true;

            Toggle guideToggle = AddToggle(uiParent, "Show rotation axis / reflection plane", rotationAxisColor, value =>
            {
                showAnimationGuides = value;
                animationGuideRoot.gameObject.SetActive(value && guideAnimationInProgress);
            });
            SetRect(guideToggle.GetComponent<RectTransform>(), new Vector2(16, -919), new Vector2(300, 27), new Vector2(0, 1));
            guideToggle.isOn = showAnimationGuides;

            Text cameraTitle = AddText(uiParent, "Camera pose  (JSON text)", 14, TextAnchor.MiddleLeft);
            SetRect(cameraTitle.rectTransform, new Vector2(16, -954), new Vector2(300, 22), new Vector2(0, 1));

            Button save = AddButton(uiParent, "Save", SaveCameraPose);
            Button load = AddButton(uiParent, "Load", LoadCameraPose);
            Button copy = AddButton(uiParent, "Copy", CopyCameraPose);
            Button paste = AddButton(uiParent, "Paste", PasteCameraPose);
            Button apply = AddButton(uiParent, "Apply", ApplyCameraPoseText);
            SetRect(save.GetComponent<RectTransform>(), new Vector2(16, -982), new Vector2(54, 28), new Vector2(0, 1));
            SetRect(load.GetComponent<RectTransform>(), new Vector2(76, -982), new Vector2(54, 28), new Vector2(0, 1));
            SetRect(copy.GetComponent<RectTransform>(), new Vector2(136, -982), new Vector2(54, 28), new Vector2(0, 1));
            SetRect(paste.GetComponent<RectTransform>(), new Vector2(196, -982), new Vector2(54, 28), new Vector2(0, 1));
            SetRect(apply.GetComponent<RectTransform>(), new Vector2(256, -982), new Vector2(54, 28), new Vector2(0, 1));

            cameraStateField = AddInputField(uiParent, orbitCamera.ExportState());
            SetRect(cameraStateField.GetComponent<RectTransform>(), new Vector2(16, -1016), new Vector2(300, 34), new Vector2(0, 1));

            SetGroupMode(groupMode);
        }

        private const string CameraPosePrefsKey = "S4Viewer.CameraPose";

        private void SaveCameraPose()
        {
            string text = orbitCamera.ExportState();
            PlayerPrefs.SetString(CameraPosePrefsKey, text);
            PlayerPrefs.Save();
            cameraStateField.text = text;
        }

        private void LoadCameraPose()
        {
            if (!PlayerPrefs.HasKey(CameraPosePrefsKey)) return;
            cameraStateField.text = PlayerPrefs.GetString(CameraPosePrefsKey);
            ApplyCameraPoseText();
        }

        private void CopyCameraPose()
        {
            cameraStateField.text = orbitCamera.ExportState();
#if UNITY_WEBGL && !UNITY_EDITOR
            S4ClipboardWrite(cameraStateField.text);
#else
            GUIUtility.systemCopyBuffer = cameraStateField.text;
#endif
        }

        private void PasteCameraPose()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            S4ClipboardRead(gameObject.name);
#else
            ReceiveClipboardText(GUIUtility.systemCopyBuffer);
#endif
        }

        public void ReceiveClipboardText(string text)
        {
            if (cameraStateField == null || string.IsNullOrEmpty(text)) return;
            cameraStateField.text = text.Trim();
            cameraStateField.ActivateInputField();
            cameraStateField.MoveTextEnd(false);
        }

        private void ApplyCameraPoseText()
        {
            if (!orbitCamera.ImportState(cameraStateField.text)) return;
            autoRotateDegreesPerSecond = orbitCamera.AutoRotateSpeed;
            autoRotationSlider.value = orbitCamera.AutoRotateSpeed;
            autoRotationToggle.isOn = orbitCamera.AutoRotateEnabled;
        }

        private void ToggleMultiplicationSide()
        {
            if (animationRoutine != null) return;
            multiplyOnRight = !multiplyOnRight;
            UpdateMultiplicationSideButton();
            statusText.text = (multiplyOnRight ? "right multiplication" : "left multiplication") +
                              "\ncurrent: " + currentPermutation.CycleNotation;
        }

        private void UpdateMultiplicationSideButton()
        {
            if (multiplySideButton == null) return;
            Text label = multiplySideButton.GetComponentInChildren<Text>();
            label.text = "Right multiply: " + (multiplyOnRight ? "ON" : "OFF");
        }

        private void SelectPermutation(Permutation permutation)
        {
            if (!IsAllowedInMode(permutation)) return;
            if (permutation.Type == CycleType.Identity)
            {
                pendingPermutations.Clear();
                if (animationRoutine != null) StopCoroutine(animationRoutine);
                animationRoutine = null;
                HideAnimationGuides();
                poseText.text = "e";
            }
            pendingPermutations.Enqueue(permutation);
            if (animationRoutine == null)
                animationRoutine = StartCoroutine(AnimateQueue());
        }

        private IEnumerator AnimateQueue()
        {
            while (pendingPermutations.Count > 0)
            {
                Permutation operation = pendingPermutations.Dequeue();
                Permutation result = operation.Type == CycleType.Identity
                    ? Permutation.Identity
                    : multiplyOnRight
                        ? currentPermutation.ComposeOnLeftOf(operation)
                        : operation.ComposeOnLeftOf(currentPermutation);
                string action = operation.Type == CycleType.Identity ? "reset to identity" : operation.IsEven ? "rotation" :
                    operation.Type == CycleType.FourCycle ? "reflection + 90 deg rotation" : "reflection";
                statusText.text = "apply " + operation.CycleNotation + "  ->  " + result.CycleNotation +
                                  "\n" + action + (pendingPermutations.Count > 0 ? "  (queued)" : "");
                if (operation.Type != CycleType.Identity)
                    poseText.text = (multiplyOnRight
                        ? currentPermutation.CycleNotation + " \u00b7 " + operation.CycleNotation
                        : operation.CycleNotation + " \u00b7 " + currentPermutation.CycleNotation) +
                                    " = " + result.CycleNotation;
                yield return AnimateOperation(operation);
                currentPermutation = result;
                poseText.text = result.CycleNotation;
            }
            animationRoutine = null;
        }

        private IEnumerator AnimateOperation(Permutation operation)
        {
            Matrix4x4 start = currentMatrix;
            Matrix4x4[] individualStarts = individualBlockMatrices.ToArray();
            Matrix4x4[] individualFrame = new Matrix4x4[individualStarts.Length];
            Pose delta = Pose.For(operation);
            ShowAnimationGuides(operation, delta, start);
            float time = 0f;
            while (time < animationDuration)
            {
                time += Time.deltaTime;
                float t = Mathf.Clamp01(time / Mathf.Max(animationDuration, 0.001f));
                float smooth = t * t * (3f - 2f * t);
                Matrix4x4 partial = delta.At(smooth).ToMatrix();
                ApplyMatrix(operation.Type == CycleType.Identity
                    ? LerpMatrix(start, Matrix4x4.identity, smooth)
                    : multiplyOnRight ? start * partial : partial * start);
                for (int i = 0; i < individualFrame.Length; i++)
                {
                    if (operation.Type == CycleType.Identity)
                        individualFrame[i] = LerpMatrix(individualStarts[i],
                            Pose.For(individualRepresentatives[i]).ToMatrix(), smooth);
                    else
                        individualFrame[i] = multiplyOnRight
                            ? individualStarts[i] * partial
                            : partial * individualStarts[i];
                }
                ApplyIndividualBlockMatrices(individualFrame);
                yield return null;
            }

            Matrix4x4 operationMatrix = Pose.For(operation).ToMatrix();
            Matrix4x4 final = operation.Type == CycleType.Identity
                ? Matrix4x4.identity
                : multiplyOnRight ? start * operationMatrix : operationMatrix * start;
            ApplyMatrix(final);
            for (int i = 0; i < individualFrame.Length; i++)
                individualFrame[i] = operation.Type == CycleType.Identity
                    ? Pose.For(individualRepresentatives[i]).ToMatrix()
                    : multiplyOnRight
                        ? individualStarts[i] * operationMatrix
                        : operationMatrix * individualStarts[i];
            ApplyIndividualBlockMatrices(individualFrame);
            HideAnimationGuides();
        }

        private static Matrix4x4 LerpMatrix(Matrix4x4 from, Matrix4x4 to, float t)
        {
            Matrix4x4 result = new Matrix4x4();
            for (int i = 0; i < 16; i++) result[i] = Mathf.Lerp(from[i], to[i], t);
            return result;
        }

        private void ApplyMatrix(Matrix4x4 matrix)
        {
            currentMatrix = matrix;
            Vector3[] points = Vertices.Select(matrix.MultiplyVector).ToArray();
            for (int i = 0; i < 4; i++) activeVertices[i].localPosition = points[i];

            int edge = 0;
            for (int a = 0; a < 4; a++)
            for (int b = a + 1; b < 4; b++)
                SetCylinder(activeEdges[edge++], points[a], points[b], edgeRadius);

            for (int i = 0; i < activeRegionMeshes.Count; i++)
            {
                Permutation chamber = modeChambers[i];
                Vector3[] tri = FundamentalTriangle.Select(chamber.Transform)
                    .Select(matrix.MultiplyVector).ToArray();
                SetDoubleSidedTriangle(activeRegionMeshes[i], tri);
            }

        }

        private void ApplyIndividualBlockMatrices(IList<Matrix4x4> blockMatrices)
        {
            for (int i = 0; i < individualMovingRegionMeshes.Count; i++)
            {
                int blockIndex = individualMeshBlockIndices[i];
                Matrix4x4 chamberMatrix = Pose.For(individualMeshChambers[i]).ToMatrix();
                Matrix4x4 matrix = blockMatrices[blockIndex] * chamberMatrix;
                Vector3[] tri = FundamentalTriangle.Select(matrix.MultiplyVector).ToArray();
                SetDoubleSidedTriangle(individualMovingRegionMeshes[i], tri);
            }
            individualBlockMatrices.Clear();
            individualBlockMatrices.AddRange(blockMatrices);
        }

        private static void SetCylinder(Transform cylinder, Vector3 a, Vector3 b, float radius)
        {
            Vector3 delta = b - a;
            cylinder.localPosition = (a + b) * 0.5f;
            cylinder.localRotation = Quaternion.FromToRotation(Vector3.up, delta);
            cylinder.localScale = new Vector3(radius, delta.magnitude * 0.5f, radius);
        }

        private static Mesh CreateTriangle(string name, Transform parent, Color color, Vector3[] points)
        {
            GameObject go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            Mesh mesh = new Mesh { name = name + " mesh" };
            SetDoubleSidedTriangle(mesh, points);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = CreateFlatMaterial(color);
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return mesh;
        }

        private static void SetDoubleSidedTriangle(Mesh mesh, Vector3[] p)
        {
            mesh.Clear();
            mesh.vertices = new[] { p[0], p[1], p[2], p[0], p[2], p[1] };
            mesh.triangles = new[] { 0, 1, 2, 3, 4, 5 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        private Material CreateMaterial(Color color)
        {
            bool transparent = color.a < 0.999f;
            Shader shader = Resources.Load<Shader>(transparent ? "S4LitTransparent" : "S4LitOpaque");
            if (shader == null)
            {
                Debug.LogError("S4 lighting shader is missing from Assets/Resources.");
                shader = Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
            }
            var material = new Material(shader) { color = color };
            material.renderQueue = transparent
                ? (int)UnityEngine.Rendering.RenderQueue.Transparent
                : (int)UnityEngine.Rendering.RenderQueue.Geometry;
            ApplyLambertSettings(material);
            lambertMaterials.Add(material);
            return material;
        }

        private void ApplyLambertSettings(Material material)
        {
            if (material.HasProperty("_Brightness"))
                material.SetFloat("_Brightness", lambertBrightness);
            if (material.HasProperty("_DiffuseStrength"))
                material.SetFloat("_DiffuseStrength", lambertDiffuseStrength);
            if (material.HasProperty("_ConstantLight"))
                material.SetFloat("_ConstantLight", lambertConstantLight);
        }

        private static Material CreateFlatMaterial(Color color)
        {
            Shader shader = Resources.Load<Shader>("S4FlatTransparent");
            if (shader == null)
            {
                Debug.LogError("S4 flat shader is missing from Assets/Resources.");
                shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
            }
            var material = new Material(shader) { color = color };
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return material;
        }

        private Color ClassColor(CycleType type)
        {
            switch (type)
            {
                case CycleType.Identity: return identityColor;
                case CycleType.Transposition: return transpositionColor;
                case CycleType.DoubleTransposition: return doubleTranspositionColor;
                case CycleType.ThreeCycle: return threeCycleColor;
                default: return fourCycleColor;
            }
        }

        private static string TypeLabel(CycleType type)
        {
            switch (type)
            {
                case CycleType.Identity: return "identity  1^4";
                case CycleType.Transposition: return "transpositions  2,1,1";
                case CycleType.DoubleTransposition: return "double trans.  2,2";
                case CycleType.ThreeCycle: return "3-cycles  3,1";
                default: return "4-cycles  4";
            }
        }

        private static int TypeCount(CycleType type)
        {
            switch (type)
            {
                case CycleType.Identity: return 1;
                case CycleType.Transposition: return 6;
                case CycleType.DoubleTransposition: return 3;
                case CycleType.ThreeCycle: return 8;
                default: return 6;
            }
        }

        private void SetGroupMode(GroupMode mode)
        {
            if (animationRoutine != null) StopCoroutine(animationRoutine);
            animationRoutine = null;
            HideAnimationGuides();
            pendingPermutations.Clear();
            groupMode = mode;
            currentPermutation = Permutation.Identity;
            currentMatrix = Matrix4x4.identity;
            poseText.text = "e";
            statusText.text = mode + " mode\ncurrent: e";
            RebuildModeGeometry();
            ResetIndividualMovingRegions();
            foreach (KeyValuePair<Permutation, Button> entry in permutationButtons)
                entry.Value.interactable = IsAllowedInMode(entry.Key);
        }

        private bool IsAllowedInMode(Permutation p)
        {
            switch (groupMode)
            {
                case GroupMode.A4: return p.IsEven;
                case GroupMode.V4: return p.Type == CycleType.Identity || p.Type == CycleType.DoubleTransposition;
                default: return true;
            }
        }

        private List<Permutation> GetSubgroupElements()
        {
            return Permutation.All().Where(IsAllowedInMode).ToList();
        }

        private List<Permutation> GetModeChambers()
        {
            List<Permutation> all = Permutation.All().ToList();
            if (groupMode == GroupMode.A4)
                return new List<Permutation> { Permutation.Identity, all.First(p => p.CycleNotation == "(12)") };
            if (groupMode == GroupMode.V4)
                return all.Where(p => p.Map[3] == 3).ToList();
            return new List<Permutation> { Permutation.Identity };
        }

        private List<List<Permutation>> GetConjugacyClasses()
        {
            List<Permutation> subgroup = GetSubgroupElements();
            var byKey = subgroup.ToDictionary(p => p.Key);
            var unseen = new HashSet<string>(byKey.Keys);
            var classes = new List<List<Permutation>>();
            foreach (Permutation g in subgroup)
            {
                if (!unseen.Contains(g.Key)) continue;
                var keys = new HashSet<string>();
                foreach (Permutation h in subgroup)
                {
                    Permutation conjugate = h.ComposeOnLeftOf(g.ComposeOnLeftOf(h.Inverse()));
                    keys.Add(conjugate.Key);
                }
                List<Permutation> conjugacyClass = keys.Select(key => byKey[key])
                    .OrderBy(p => p.CycleNotation).ToList();
                classes.Add(conjugacyClass);
                unseen.ExceptWith(keys);
            }
            return classes;
        }

        private void RebuildConjugacyOverlays()
        {
            foreach (GameObject go in conjugacyGroups) Destroy(go);
            conjugacyGroups.Clear();
            List<List<Permutation>> classes = GetConjugacyClasses();
            for (int classIndex = 0; classIndex < classes.Count; classIndex++)
            {
                Color color = ConjugacyColor(classIndex, classes[classIndex][0]);
                GameObject group = new GameObject(groupMode + " class " + (classIndex + 1));
                group.transform.SetParent(overlayRoot, false);
                foreach (Permutation element in classes[classIndex])
                for (int chamberIndex = 0; chamberIndex < modeChambers.Count; chamberIndex++)
                {
                    Permutation chamber = modeChambers[chamberIndex];
                    Vector3[] points = FundamentalTriangle
                        .Select(chamber.Transform).Select(element.Transform).ToArray();
                    Color chamberColor = chamberIndex == 0 ? color : Darkened(color, 0.5f);
                    chamberColor.a *= 0.58f;
                    CreateTriangle(element.CycleNotation + " chamber " + chamberIndex, group.transform,
                        chamberColor, points);
                }
                group.SetActive(false);
                conjugacyGroups.Add(group);
            }
            RebuildConjugacyToggleUi(classes);
        }

        private void RebuildConjugacyToggleUi(List<List<Permutation>> classes)
        {
            if (conjugacyToggleContainer == null) return;
            for (int i = conjugacyToggleContainer.childCount - 1; i >= 0; i--)
                Destroy(conjugacyToggleContainer.GetChild(i).gameObject);
            for (int i = 0; i < classes.Count; i++)
            {
                int captured = i;
                string label = groupMode == GroupMode.S4
                    ? TypeLabel(classes[i][0].Type) + "  [" + classes[i].Count + "]"
                    : "{" + string.Join(",", classes[i].Select(p => p.CycleNotation)) + "}";
                Toggle toggle = AddToggle(conjugacyToggleContainer, label, ConjugacyColor(i, classes[i][0]),
                    value => conjugacyGroups[captured].SetActive(value));
                SetRect(toggle.GetComponent<RectTransform>(), new Vector2(0, -i * 24f), new Vector2(300, 23), new Vector2(0, 1));
            }
        }

        private void BuildIndividualRegionOverlays()
        {
            foreach (GameObject oldGroup in individualRegionGroups) Destroy(oldGroup);
            individualRegionGroups.Clear();
            individualMovingRegionMeshes.Clear();
            individualRepresentatives.Clear();
            individualBlockMatrices.Clear();
            individualMeshBlockIndices.Clear();
            individualMeshChambers.Clear();

            List<Permutation> representatives = GetSubgroupElements();
            for (int representativeIndex = 0; representativeIndex < representatives.Count; representativeIndex++)
            {
                Permutation representative = representatives[representativeIndex];
                individualRepresentatives.Add(representative);
                individualBlockMatrices.Add(Pose.For(representative).ToMatrix());
                GameObject group = new GameObject(groupMode + " region at " + representative.CycleNotation);
                group.transform.SetParent(overlayRoot, false);
                for (int chamberIndex = 0; chamberIndex < modeChambers.Count; chamberIndex++)
                {
                    // The larger fundamental region is the right-hand block g*h:
                    // {g,g(12)} in A4 and the six g*h chambers in V4.
                    Permutation location = representative.ComposeOnLeftOf(modeChambers[chamberIndex]);
                    Color movingColor = chamberIndex == 0
                        ? fundamentalRegionColor
                        : Darkened(fundamentalRegionColor, 0.48f);
                    movingColor.a *= 0.58f;
                    Color fixedColor = Darkened(movingColor, 0.52f);
                    fixedColor.a *= 0.3f;
                    Vector3[] points = FundamentalTriangle.Select(location.Transform).ToArray();
                    CreateTriangle("Fixed " + location.CycleNotation, group.transform, fixedColor, points);
                    Renderer fixedRenderer = group.transform.GetChild(group.transform.childCount - 1).GetComponent<Renderer>();
                    fixedRenderer.sharedMaterial.renderQueue = 2998;
                    Mesh movingMesh = CreateTriangle("Moving " + location.CycleNotation, group.transform, movingColor, points);
                    Renderer movingRenderer = group.transform.GetChild(group.transform.childCount - 1).GetComponent<Renderer>();
                    movingRenderer.sharedMaterial.renderQueue = 3000;
                    individualMovingRegionMeshes.Add(movingMesh);
                    individualMeshBlockIndices.Add(representativeIndex);
                    individualMeshChambers.Add(modeChambers[chamberIndex]);
                }
                group.SetActive(false);
                individualRegionGroups.Add(group);
            }
            RebuildIndividualToggleUi(representatives);
        }

        private void RebuildIndividualToggleUi(List<Permutation> representatives)
        {
            if (individualToggleContainer == null) return;
            for (int i = individualToggleContainer.childCount - 1; i >= 0; i--)
                Destroy(individualToggleContainer.GetChild(i).gameObject);
            for (int i = 0; i < representatives.Count; i++)
            {
                int captured = i;
                int column = i % 4;
                int row = i / 4;
                Toggle toggle = AddToggle(individualToggleContainer, representatives[i].CycleNotation,
                    fundamentalRegionColor, value => individualRegionGroups[captured].SetActive(value));
                SetRect(toggle.GetComponent<RectTransform>(), new Vector2(column * 76, -row * 26),
                    new Vector2(72, 23), new Vector2(0, 1));
            }
        }

        private void ResetIndividualMovingRegions()
        {
            if (individualMovingRegionMeshes.Count == 0) return;
            var matrices = individualRepresentatives.Select(p => Pose.For(p).ToMatrix()).ToArray();
            ApplyIndividualBlockMatrices(matrices);
        }

        private Color ConjugacyColor(int index, Permutation representative)
        {
            Color[] palette = { identityColor, doubleTranspositionColor, threeCycleColor,
                fourCycleColor, transpositionColor, new Color(0.1f, 0.78f, 0.76f, 0.7f) };
            if (groupMode == GroupMode.S4) return ClassColor(representative.Type);
            return palette[index % palette.Length];
        }

        private static Color Darkened(Color color, float multiplier)
        {
            return new Color(color.r * multiplier, color.g * multiplier, color.b * multiplier, color.a);
        }

        #region Minimal runtime UI helpers
        private static GameObject UiObject(string name, Transform parent, params Type[] components)
        {
            // UI controls need a RectTransform even when the requested component
            // (or an empty layout container) does not require one automatically.
            var go = new GameObject(name, typeof(RectTransform));
            foreach (Type component in components)
                if (component != typeof(RectTransform)) go.AddComponent(component);
            go.transform.SetParent(parent, false);
            return go;
        }

        private static Font UiFont => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        private static Text AddText(Transform parent, string value, int size, TextAnchor alignment)
        {
            GameObject go = UiObject("Text", parent, typeof(Text));
            Text text = go.GetComponent<Text>();
            text.font = UiFont;
            text.text = value;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = new Color(0.92f, 0.94f, 1f);
            return text;
        }

        private static Button AddButton(Transform parent, string label, UnityEngine.Events.UnityAction action)
        {
            GameObject go = UiObject("Button " + label, parent, typeof(Image), typeof(Button));
            go.GetComponent<Image>().color = Color.white;
            Button button = go.GetComponent<Button>();
            button.targetGraphic = go.GetComponent<Image>();
            button.onClick.AddListener(action);
            Text text = AddText(go.transform, label, 12, TextAnchor.MiddleCenter);
            text.color = new Color(0.04f, 0.05f, 0.07f);
            Stretch(text.rectTransform, 3f);
            return button;
        }

        private static Toggle AddToggle(Transform parent, string label, Color color,
            UnityEngine.Events.UnityAction<bool> action)
        {
            GameObject go = UiObject("Toggle " + label, parent, typeof(Toggle));
            GameObject background = UiObject("Background", go.transform, typeof(Image));
            SetRect(background.GetComponent<RectTransform>(), Vector2.zero, new Vector2(22, 22), new Vector2(0, 0.5f));
            background.GetComponent<Image>().color = new Color(0.16f, 0.18f, 0.23f);
            GameObject check = UiObject("Checkmark", background.transform, typeof(Image));
            Stretch(check.GetComponent<RectTransform>(), 4f);
            check.GetComponent<Image>().color = color;
            Text text = AddText(go.transform, label, 12, TextAnchor.MiddleLeft);
            SetRect(text.rectTransform, new Vector2(28, 0), new Vector2(-28, 0), new Vector2(0, 0));
            text.rectTransform.anchorMax = Vector2.one;
            Toggle toggle = go.GetComponent<Toggle>();
            toggle.targetGraphic = background.GetComponent<Image>();
            toggle.graphic = check.GetComponent<Image>();
            toggle.isOn = false;
            toggle.onValueChanged.AddListener(action);
            return toggle;
        }

        private static InputField AddInputField(Transform parent, string value)
        {
            GameObject go = UiObject("Camera pose text", parent, typeof(Image), typeof(InputField));
            Image background = go.GetComponent<Image>();
            background.color = new Color(0.92f, 0.94f, 0.98f);

            Text text = AddText(go.transform, value, 10, TextAnchor.MiddleLeft);
            text.name = "Text";
            text.color = new Color(0.04f, 0.05f, 0.07f);
            text.supportRichText = false;
            Stretch(text.rectTransform, 6f);

            InputField field = go.GetComponent<InputField>();
            field.targetGraphic = background;
            field.textComponent = text;
            field.lineType = InputField.LineType.SingleLine;
            field.text = value;
            return field;
        }

        private static Slider AddSlider(Transform parent, float min, float max, float value,
            UnityEngine.Events.UnityAction<float> action)
        {
            GameObject go = UiObject("Auto rotation slider", parent, typeof(Slider));
            GameObject background = UiObject("Background", go.transform, typeof(Image));
            Stretch(background.GetComponent<RectTransform>(), 8f);
            background.GetComponent<Image>().color = new Color(0.16f, 0.18f, 0.23f);
            GameObject fillArea = UiObject("Fill Area", go.transform);
            Stretch(fillArea.GetComponent<RectTransform>(), 8f);
            GameObject fill = UiObject("Fill", fillArea.transform, typeof(Image));
            Stretch(fill.GetComponent<RectTransform>(), 0f);
            fill.GetComponent<Image>().color = new Color(0.25f, 0.68f, 1f);
            GameObject handleArea = UiObject("Handle Slide Area", go.transform);
            Stretch(handleArea.GetComponent<RectTransform>(), 8f);
            GameObject handle = UiObject("Handle", handleArea.transform, typeof(Image));
            handle.GetComponent<RectTransform>().sizeDelta = new Vector2(18, 24);
            handle.GetComponent<Image>().color = Color.white;
            Slider slider = go.GetComponent<Slider>();
            slider.fillRect = fill.GetComponent<RectTransform>();
            slider.handleRect = handle.GetComponent<RectTransform>();
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            slider.onValueChanged.AddListener(action);
            return slider;
        }

        private static Scrollbar AddVerticalScrollbar(Transform parent)
        {
            GameObject go = UiObject("Scrollbar Vertical", parent, typeof(Image), typeof(Scrollbar));
            go.GetComponent<Image>().color = new Color(0.1f, 0.12f, 0.16f, 0.9f);
            GameObject slidingArea = UiObject("Sliding Area", go.transform);
            Stretch(slidingArea.GetComponent<RectTransform>(), 2f);
            GameObject handle = UiObject("Handle", slidingArea.transform, typeof(Image));
            Stretch(handle.GetComponent<RectTransform>(), 0f);
            handle.GetComponent<Image>().color = new Color(0.55f, 0.62f, 0.72f, 0.95f);
            Scrollbar scrollbar = go.GetComponent<Scrollbar>();
            scrollbar.handleRect = handle.GetComponent<RectTransform>();
            scrollbar.targetGraphic = handle.GetComponent<Image>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.size = 0.25f;
            return scrollbar;
        }

        private static void SetRect(RectTransform rect, Vector2 anchoredPosition, Vector2 size, Vector2 anchor)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.one * inset;
            rect.offsetMax = Vector2.one * -inset;
        }
        #endregion

        private enum GroupMode { S4, A4, V4 }
        private enum CycleType { Identity, Transposition, DoubleTransposition, ThreeCycle, FourCycle }

        private sealed class Permutation
        {
            public static readonly Permutation Identity = new Permutation(new[] { 0, 1, 2, 3 });
            public readonly int[] Map;
            public readonly bool IsEven;
            public readonly int Order;
            public readonly CycleType Type;
            public readonly string CycleNotation;
            public string Key => string.Join("", Map);

            private Permutation(int[] map)
            {
                Map = map;
                int inversions = 0;
                for (int i = 0; i < 4; i++)
                for (int j = i + 1; j < 4; j++)
                    if (map[i] > map[j]) inversions++;
                IsEven = inversions % 2 == 0;

                var lengths = new List<int>();
                var visited = new bool[4];
                var notation = "";
                for (int i = 0; i < 4; i++)
                {
                    if (visited[i]) continue;
                    var cycle = new List<int>();
                    int k = i;
                    do { visited[k] = true; cycle.Add(k + 1); k = map[k]; } while (!visited[k]);
                    lengths.Add(cycle.Count);
                    if (cycle.Count > 1) notation += "(" + string.Join("", cycle) + ")";
                }
                if (notation.Length == 0) notation = "e";
                CycleNotation = notation;
                lengths.Sort();
                lengths.Reverse();
                if (lengths[0] == 1) Type = CycleType.Identity;
                else if (lengths[0] == 2 && lengths.Count(x => x == 2) == 1) Type = CycleType.Transposition;
                else if (lengths[0] == 2) Type = CycleType.DoubleTransposition;
                else if (lengths[0] == 3) Type = CycleType.ThreeCycle;
                else Type = CycleType.FourCycle;
                Order = lengths.Aggregate(1, Lcm);
            }

            public Vector3 Transform(Vector3 point)
            {
                // M = (1/4) sum_i target_i source_i^T, since sum(v_i v_i^T) = 4I.
                Vector3 result = Vector3.zero;
                for (int i = 0; i < 4; i++) result += Vertices[Map[i]] * Vector3.Dot(Vertices[i], point) * 0.25f;
                return result;
            }

            public Permutation ComposeOnLeftOf(Permutation current)
            {
                // Matrix(this) * Matrix(current): i -> this(current(i)).
                var composed = new int[4];
                for (int i = 0; i < 4; i++) composed[i] = Map[current.Map[i]];
                return new Permutation(composed);
            }

            public Permutation Inverse()
            {
                var inverse = new int[4];
                for (int i = 0; i < 4; i++) inverse[Map[i]] = i;
                return new Permutation(inverse);
            }

            public static IEnumerable<Permutation> All()
            {
                // Lexicographic order keeps the button layout stable between runs.
                int[] a = { 0, 1, 2, 3 };
                do { yield return new Permutation((int[])a.Clone()); } while (NextPermutation(a));
            }

            private static bool NextPermutation(int[] a)
            {
                int i = a.Length - 2;
                while (i >= 0 && a[i] >= a[i + 1]) i--;
                if (i < 0) return false;
                int j = a.Length - 1;
                while (a[j] <= a[i]) j--;
                int temp = a[i]; a[i] = a[j]; a[j] = temp;
                Array.Reverse(a, i + 1, a.Length - i - 1);
                return true;
            }

            private static int Lcm(int a, int b) => a * b / Gcd(a, b);
            private static int Gcd(int a, int b) { while (b != 0) { int t = a % b; a = b; b = t; } return a; }
        }

        private struct Pose
        {
            private readonly Quaternion rotation;
            private readonly Vector3 reflectionNormal;
            private readonly float reflectionAmount;
            private readonly bool hasReflection;

            public static Pose Identity => new Pose(Quaternion.identity, Vector3.up, 0f, false);
            public Vector3 ReflectionNormal => reflectionNormal;
            public Vector3 RotationAxis
            {
                get
                {
                    rotation.ToAngleAxis(out _, out Vector3 axis);
                    return axis.sqrMagnitude > 1e-8f ? axis.normalized : Vector3.up;
                }
            }
            private Pose(Quaternion rotation, Vector3 normal, float amount, bool reflection)
            { this.rotation = rotation; reflectionNormal = normal; reflectionAmount = amount; hasReflection = reflection; }

            public static Pose For(Permutation p)
            {
                if (p.IsEven)
                    return new Pose(RotationFromTransform(p), Vector3.up, 0f, false);

                // Odd tetrahedral symmetries have one -1 eigenvector.  Reflect in
                // its perpendicular plane; the remaining proper part is identity
                // for a transposition and a quarter-turn for a 4-cycle.
                Vector3 n = FindMinusOneEigenvector(p);
                Quaternion properRotation = RotationAfterReflection(p, n);
                return new Pose(properRotation, n, 1f, true);
            }

            public Pose At(float t)
            {
                return new Pose(Quaternion.Slerp(Quaternion.identity, rotation, t), reflectionNormal,
                    reflectionAmount * t, hasReflection);
            }

            public Vector3 Apply(Vector3 point)
            {
                if (hasReflection)
                    point -= 2f * reflectionAmount * Vector3.Dot(reflectionNormal, point) * reflectionNormal;
                return rotation * point;
            }

            public Matrix4x4 ToMatrix()
            {
                Vector3 right = Apply(Vector3.right);
                Vector3 up = Apply(Vector3.up);
                Vector3 forward = Apply(Vector3.forward);
                Matrix4x4 matrix = Matrix4x4.identity;
                matrix.SetColumn(0, new Vector4(right.x, right.y, right.z, 0f));
                matrix.SetColumn(1, new Vector4(up.x, up.y, up.z, 0f));
                matrix.SetColumn(2, new Vector4(forward.x, forward.y, forward.z, 0f));
                return matrix;
            }

            private static Quaternion RotationFromTransform(Permutation p)
            {
                Vector3 forward = p.Transform(Vector3.forward);
                Vector3 up = p.Transform(Vector3.up);
                return Quaternion.LookRotation(forward, up);
            }

            private static Vector3 FindMinusOneEigenvector(Permutation p)
            {
                // Rows of M+I; the cross product of two independent rows spans its nullspace.
                Vector3 r0 = new Vector3(p.Transform(Vector3.right).x + 1f,
                    p.Transform(Vector3.up).x, p.Transform(Vector3.forward).x);
                Vector3 r1 = new Vector3(p.Transform(Vector3.right).y,
                    p.Transform(Vector3.up).y + 1f, p.Transform(Vector3.forward).y);
                Vector3 r2 = new Vector3(p.Transform(Vector3.right).z,
                    p.Transform(Vector3.up).z, p.Transform(Vector3.forward).z + 1f);
                Vector3[] candidates = { Vector3.Cross(r0, r1), Vector3.Cross(r0, r2), Vector3.Cross(r1, r2) };
                Vector3 best = candidates.OrderByDescending(v => v.sqrMagnitude).First();
                return best.sqrMagnitude > 1e-8f ? best.normalized : Vector3.up;
            }

            private static Quaternion RotationAfterReflection(Permutation p, Vector3 n)
            {
                Func<Vector3, Vector3> reflectedThenMapped = axis =>
                {
                    Vector3 reflected = axis - 2f * Vector3.Dot(n, axis) * n;
                    return p.Transform(reflected);
                };
                return Quaternion.LookRotation(reflectedThenMapped(Vector3.forward), reflectedThenMapped(Vector3.up));
            }
        }
    }

    public sealed class ResponsiveMenu : MonoBehaviour
    {
        private RectTransform panel;
        private Button toggleButton;
        private CanvasScaler scaler;
        private bool collapsed;
        private bool wasPortrait;
        private int lastWidth;
        private int lastHeight;

        public void Configure(RectTransform menuPanel, Button button, CanvasScaler canvasScaler)
        {
            panel = menuPanel;
            toggleButton = button;
            scaler = canvasScaler;
            wasPortrait = Screen.height > Screen.width;
            collapsed = wasPortrait;
            ApplyLayout();
        }

        public void Toggle()
        {
            collapsed = !collapsed;
            ApplyLayout();
        }

        private void Update()
        {
            if (panel == null || (Screen.width == lastWidth && Screen.height == lastHeight)) return;
            bool portrait = Screen.height > Screen.width;
            if (portrait && !wasPortrait) collapsed = true;
            wasPortrait = portrait;
            ApplyLayout();
        }

        private void ApplyLayout()
        {
            if (panel == null) return;
            lastWidth = Screen.width;
            lastHeight = Screen.height;
            bool portrait = Screen.height > Screen.width;
            scaler.matchWidthOrHeight = portrait ? 0.15f : 0.5f;
            float width = portrait ? 330f : 350f;
            panel.sizeDelta = new Vector2(width, 0f);
            panel.gameObject.SetActive(!collapsed);

            RectTransform buttonRect = toggleButton.GetComponent<RectTransform>();
            buttonRect.anchoredPosition = new Vector2(collapsed ? 8f : width - 46f, -8f);
            Text label = toggleButton.GetComponentInChildren<Text>();
            label.text = collapsed ? "MENU" : "<<";
            label.fontSize = collapsed ? 10 : 14;
        }
    }

    public sealed class WorldVertexLabel : MonoBehaviour
    {
        private Transform follow;
        private Vector3 baseLocalPosition;
        private Vector2 screenOffset;

        public void Configure(Transform followTarget, Vector3 localPosition, Vector2 offset)
        {
            follow = followTarget;
            baseLocalPosition = localPosition;
            screenOffset = offset;
        }

        private void LateUpdate()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            Vector3 anchor = follow != null ? follow.position : transform.parent.TransformPoint(baseLocalPosition);
            transform.position = anchor + cam.transform.right * screenOffset.x + cam.transform.up * screenOffset.y;
            transform.rotation = cam.transform.rotation;
        }
    }

    public sealed class OrbitCamera : MonoBehaviour
    {
        public float AutoRotateSpeed { get; set; }
        public bool AutoRotateEnabled { get; set; } = true;
        private Vector3 target;
        private float distance;
        private float pitch;
        private float yaw;
        private bool pinchActive;
        private int pinchFingerA = -1;
        private int pinchFingerB = -1;
        private float previousPinchSeparation;
        private float suppressMouseUntil;

        public void Configure(Vector3 targetPoint, float initialDistance, float initialPitch, float initialYaw, float autoSpeed)
        {
            target = targetPoint;
            distance = initialDistance;
            pitch = initialPitch;
            yaw = initialYaw;
            AutoRotateSpeed = autoSpeed;
            AutoRotateEnabled = true;
            Input.simulateMouseWithTouches = false;
            UpdateTransform();
        }

        public string ExportState()
        {
            return JsonUtility.ToJson(new CameraState
            {
                version = 1,
                yaw = yaw,
                pitch = pitch,
                distance = distance,
                autoRotateSpeed = AutoRotateSpeed,
                autoRotateEnabled = AutoRotateEnabled
            });
        }

        public bool ImportState(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                CameraState state = JsonUtility.FromJson<CameraState>(json);
                if (state == null || state.version != 1 || !IsFinite(state.yaw) || !IsFinite(state.pitch) ||
                    !IsFinite(state.distance) || !IsFinite(state.autoRotateSpeed)) return false;
                yaw = state.yaw;
                pitch = Mathf.Clamp(state.pitch, -89.99f, 89.99f);
                distance = Mathf.Clamp(state.distance, 3.2f, 30f);
                AutoRotateSpeed = Mathf.Clamp(state.autoRotateSpeed, -30f, 30f);
                AutoRotateEnabled = state.autoRotateEnabled;
                UpdateTransform();
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private void LateUpdate()
        {
            bool isInteracting = false;
            if (Input.touchCount >= 2)
            {
                Touch first = Input.GetTouch(0);
                Touch second = Input.GetTouch(1);
                bool overUi = IsTouchOverUi(first) || IsTouchOverUi(second);
                isInteracting = true;
                suppressMouseUntil = Time.unscaledTime + 0.35f;

                int fingerA = Mathf.Min(first.fingerId, second.fingerId);
                int fingerB = Mathf.Max(first.fingerId, second.fingerId);
                float currentSeparation = Vector2.Distance(first.position, second.position);
                bool samePinch = pinchActive && fingerA == pinchFingerA && fingerB == pinchFingerB;
                if (samePinch && !overUi)
                {
                    float referenceSize = Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height));
                    float normalizedPinch = (currentSeparation - previousPinchSeparation) / referenceSize;
                    distance = Mathf.Clamp(distance * Mathf.Exp(normalizedPinch * 2.5f), 3.2f, 30f);
                }
                pinchActive = true;
                pinchFingerA = fingerA;
                pinchFingerB = fingerB;
                previousPinchSeparation = currentSeparation;
            }
            else if (Input.touchCount == 1)
            {
                pinchActive = false;
                Touch touch = Input.GetTouch(0);
                isInteracting = true;
                suppressMouseUntil = Time.unscaledTime + 0.35f;
                if (!IsTouchOverUi(touch) && touch.phase == TouchPhase.Moved)
                {
                    yaw -= touch.deltaPosition.x * 0.2f;
                    pitch += touch.deltaPosition.y * 0.2f;
                    pitch = Mathf.Clamp(pitch, -89.99f, 89.99f);
                }
            }
            else
            {
                pinchActive = false;
                if (Time.unscaledTime < suppressMouseUntil)
                {
                    isInteracting = true;
                }
                else
                {
                    bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
                    if (Input.GetMouseButton(0))
                    {
                        isInteracting = true;
                        if (!overUi)
                        {
                            yaw += Input.GetAxis("Mouse X") * 4.5f;
                            pitch -= Input.GetAxis("Mouse Y") * 4.5f;
                            pitch = Mathf.Clamp(pitch, -89.99f, 89.99f);
                        }
                    }
                    if (!overUi)
                        distance = Mathf.Clamp(distance - Input.mouseScrollDelta.y * 0.35f, 3.2f, 30f);
                }
            }

            if (!isInteracting && AutoRotateEnabled)
                yaw += AutoRotateSpeed * Time.deltaTime;
            UpdateTransform();
        }

        private static bool IsTouchOverUi(Touch touch)
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(touch.fingerId);
        }

        private void UpdateTransform()
        {
            Quaternion orbit = Quaternion.Euler(pitch, yaw, 0f);
            transform.position = target + orbit * (Vector3.back * distance);
            transform.rotation = Quaternion.LookRotation(target - transform.position, Vector3.up);
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        [Serializable]
        private sealed class CameraState
        {
            public int version;
            public float yaw;
            public float pitch;
            public float distance;
            public float autoRotateSpeed;
            public bool autoRotateEnabled;
        }
    }
}
