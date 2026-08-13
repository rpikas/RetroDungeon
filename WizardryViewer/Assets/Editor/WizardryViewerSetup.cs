using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using TMPro;
using WizardryViewer.Unity;

namespace WizardryViewer.EditorTools
{
    /// <summary>
    /// One menu item that produces a running scene: URP configured, materials and stand-in
    /// prefabs generated, table and camera placed, components wired. No manual steps.
    ///
    /// Everything it makes is a placeholder in the right shape and at the right scale —
    /// real cardboard textures and real minis drop in on top without moving anything.
    /// </summary>
    public static class WizardryViewerSetup
    {
        private const string Root = "Assets/Generated";
        private const string ScenePath = "Assets/Scenes/Table.unity";

        // Real-world scale, so a headset user later sees a table that is actually table-sized.
        private const float CellSize = 0.0254f;   // a 1-inch dungeon tile
        // Height an imported model is fitted to BEFORE the footprint clamp shrinks it further. The
        // clamp is what actually sets final size, so this stays put when figures are scaled down —
        // reducing both would shrink twice over.
        private const float StandeeHeight = 0.022f;

        // The generated primitive figures have no footprint clamp, so their size is set here. Kept
        // in step with what the clamp leaves the imported ones, or the goblins tower over the party.
        private const float PrimitiveHeight = 0.0175f;

        // Exposed so imported models can be fitted to the same scale as the generated ones.
        public const float FigureHeightMetres = StandeeHeight;
        public const float CellSizeMetres = CellSize;

        /// <summary>
        /// Base diameter in cells. This is what actually decides wall clearance: the figure can be
        /// clamped as narrow as you like and a 0.6-cell base will still put the outer files through
        /// the corridor wall. Keep fileSpacing plus half of this under 0.5.
        /// </summary>
        public const float PlinthCells = 0.34f;

        /// <summary>
        /// Build the table without anyone having to find a menu. Runs once, after a compile,
        /// only when the scene does not already exist — so it can never clobber later work.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void AutoBuildIfMissing()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                if (File.Exists(ScenePath)) return;

                Debug.Log("[setup] no table scene found - building it now");
                try
                {
                    BuildAll();
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[setup] auto-build failed: {ex}");
                }
            };
        }

        // Registered in two places so it is findable however you look for it.
        [MenuItem("Wizardry Viewer/Build Sample Table")]
        [MenuItem("Tools/Build Wizardry Table")]
        public static void BuildAll()
        {
            // EditorSceneManager.NewScene throws in play mode, and the exception lands
            // halfway through, leaving assets created but no scene. Refuse up front.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[setup] cannot rebuild the table while in play mode. " +
                                 "Stop playback, run this again, then press Play.");
                return;
            }

            // Snapshots arrive from another process. With this off, an unfocused Editor stops
            // ticking Update, the inbox piles up, and the catch-up rule discards the whole
            // crawl as one jump the moment focus returns — the viewer looks broken instead.
            PlayerSettings.runInBackground = true;

            EnsureFolders();
            var urp = ConfigureUrp();
            var mats = BuildMaterials();
            var prefabs = BuildPrefabs(mats);
            BuildScene(mats, prefabs);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[setup] done. URP={(urp ? "configured" : "SKIPPED")}  scene={ScenePath}");
        }

        private static void EnsureFolders()
        {
            foreach (var path in new[] { Root, Root + "/Materials", Root + "/Prefabs", "Assets/Scenes" })
            {
                if (AssetDatabase.IsValidFolder(path)) continue;
                var parent = Path.GetDirectoryName(path).Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
            }
        }

        private static bool ConfigureUrp()
        {
            try
            {
                var assetPath = Root + "/UniversalRP.asset";
                var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);

                if (pipeline == null)
                {
                    var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                    AssetDatabase.CreateAsset(rendererData, Root + "/UniversalRenderer.asset");

                    // Called by reflection on purpose: UniversalRenderPipelineAsset.Create is
                    // public in some URP versions and internal in others, and a direct call
                    // would be a compile error rather than something we can fall back from.
                    var create = typeof(UniversalRenderPipelineAsset).GetMethod(
                        "Create",
                        System.Reflection.BindingFlags.Static |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic);

                    pipeline = create != null
                        ? create.Invoke(null, new object[] { rendererData }) as UniversalRenderPipelineAsset
                        : ScriptableObject.CreateInstance<UniversalRenderPipelineAsset>();

                    if (pipeline == null)
                    {
                        Debug.LogWarning("[setup] could not create a URP asset; assign one manually " +
                                         "in Project Settings > Graphics.");
                        return false;
                    }

                    AssetDatabase.CreateAsset(pipeline, assetPath);
                }

                GraphicsSettings.defaultRenderPipeline = pipeline;
                QualitySettings.renderPipeline = pipeline;
                return true;
            }
            catch (System.Exception ex)
            {
                // Non-fatal: the scene still builds, it just renders with whatever is set.
                Debug.LogWarning($"[setup] could not configure URP automatically: {ex.Message}");
                return false;
            }
        }

        private struct Mats
        {
            public Material Cardboard, CardboardDark, Table, Plastic, PlasticFoe, Paper;
            public Material Stone, Steel, Gold, Skin, Slate;
            public Material Fighter, Thief, Priest, MagicUser, Bishop;
            public Material Glass, GlassButton, GlassButtonHot;
        }

        /// <summary>
        /// A see-through material for the prompt dialog.
        ///
        /// URP ignores the alpha in _BaseColor until the material is switched to a transparent surface,
        /// and switching it means all of this: the _Surface float the inspector drives, the blend factors
        /// the shader actually samples, ZWrite off so panels do not punch holes in each other, the
        /// keyword the shader branches on, and the render queue. Set only the colour and you get a fully
        /// opaque slab with a meaningless alpha -- which is the failure this method exists to prevent.
        /// </summary>
        private static Material MakeGlass(string name, Color colour, float smoothness)
        {
            var path = $"{Root}/Materials/{name}.mat";

            // Unlike the opaque materials, an existing glass asset is UPDATED rather than left alone.
            // Transparency here is six properties, a keyword and a queue that must agree; a glass material
            // saved before any of that was right renders as an opaque slab, and silently keeping it would
            // make re-running this menu item look like it had no effect. A plain colour material has
            // nothing that can go stale, so those still early-return.
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var isNew = m == null;

            // UNLIT, unlike every other material here. The dialog is the one thing on the table that must
            // stay readable wherever the party wanders, and a lit panel takes its brightness from the lamp
            // -- which pools on the room, not on the dialog beside it. Lit looked like a black slab with
            // invisible labels two cells outside the light. Unlit also costs less per eye in a headset.
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Universal Render Pipeline/Lit")
                      ?? Shader.Find("Standard");

            if (isNew) m = new Material(shader) { name = name };
            else m.shader = shader;

            m.SetColor("_BaseColor", colour);
            m.SetColor("_Color", colour);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);

            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);   // 0 opaque, 1 transparent
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);       // alpha blend
            if (m.HasProperty("_AlphaClip")) m.SetFloat("_AlphaClip", 0f);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);

            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.DisableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)RenderQueue.Transparent;

            if (isNew) AssetDatabase.CreateAsset(m, path);
            else EditorUtility.SetDirty(m);
            return m;
        }

        private static Material MakeMaterial(string name, Color colour, float smoothness, float metallic = 0f)
        {
            var path = $"{Root}/Materials/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(shader) { name = name };
            m.SetColor("_BaseColor", colour);
            m.SetColor("_Color", colour);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        private static Mats BuildMaterials() => new Mats
        {
            // Matte, slightly warm: cardboard's whole look is low smoothness + a normal map later.
            Cardboard     = MakeMaterial("Cardboard",      new Color(0.72f, 0.58f, 0.40f), 0.05f),
            CardboardDark = MakeMaterial("CardboardDark",  new Color(0.45f, 0.35f, 0.24f), 0.05f),
            Table         = MakeMaterial("TableTop",       new Color(0.30f, 0.20f, 0.13f), 0.35f),
            Plastic       = MakeMaterial("PlasticHero",    new Color(0.20f, 0.35f, 0.70f), 0.55f),
            PlasticFoe    = MakeMaterial("PlasticFoe",     new Color(0.30f, 0.45f, 0.20f), 0.45f),
            Paper         = MakeMaterial("Paper",          new Color(0.92f, 0.90f, 0.84f), 0.05f),

            // Walls are dungeon stone, not cardboard: they have to read as walls against a
            // tan floor, and near-white ones washed out completely under the lamp.
            Stone         = MakeMaterial("Stone",          new Color(0.26f, 0.25f, 0.24f), 0.10f),
            Steel         = MakeMaterial("Steel",          new Color(0.62f, 0.64f, 0.68f), 0.75f, 0.85f),
            Gold          = MakeMaterial("Gold",           new Color(0.78f, 0.62f, 0.22f), 0.70f, 0.75f),
            Skin          = MakeMaterial("Skin",           new Color(0.80f, 0.64f, 0.50f), 0.20f),
            // Bases were near-white Paper, and six of them merged into one bright blob up close.
            Slate         = MakeMaterial("Slate",          new Color(0.19f, 0.19f, 0.21f), 0.15f),

            // The prompt dialog: dark smoked glass over the tabletop. Dark rather than pale because the
            // lamp puts a bright pool exactly where the dialog sits, and the labels are light -- a pale
            // panel gave light text on a light glow. The buttons sit a shade lighter than the panel so
            // they read as raised, and the hot one goes gold to match the lamp rather than fight it.
            Glass         = MakeGlass("Glass",             new Color(0.07f, 0.07f, 0.10f, 0.72f), 0.55f),
            GlassButton   = MakeGlass("GlassButton",       new Color(0.22f, 0.21f, 0.26f, 0.86f), 0.60f),
            GlassButtonHot = MakeGlass("GlassButtonHot",   new Color(0.64f, 0.48f, 0.16f, 0.95f), 0.70f),

            // One colour per class, so six minis in a huddle are still six people.
            Fighter       = MakeMaterial("MiniFighter",    new Color(0.62f, 0.16f, 0.14f), 0.35f),
            Thief         = MakeMaterial("MiniThief",      new Color(0.18f, 0.20f, 0.24f), 0.35f),
            Priest        = MakeMaterial("MiniPriest",     new Color(0.88f, 0.86f, 0.78f), 0.25f),
            MagicUser     = MakeMaterial("MiniMagicUser",  new Color(0.28f, 0.20f, 0.55f), 0.35f),
            Bishop        = MakeMaterial("MiniBishop",     new Color(0.35f, 0.16f, 0.42f), 0.35f),
        };

        private struct Prefabs
        {
            public GameObject FloorTile, WallPiece, Standee, StandeeFoe;
            public GameObject Fighter, Thief, Priest, MagicUser, Bishop;
        }

        /// <summary>What tells one 28mm figure from another at arm's length: outline, then kit.</summary>
        private enum Kit { None, Sword, Dagger, Staff, Mitre, MitreAndStaff, Club }

        private struct MiniSpec
        {
            public string Name;
            public Material Body;
            public Material Accent;
            public PrimitiveType Shape;   // Cube reads as armour, Capsule as a robe
            public float Girth;           // fraction of a cell wide
            public float Height;          // fraction of a 28mm figure
            public Kit Kit;
        }

        private static GameObject SavePrefab(GameObject go, string name)
        {
            var path = $"{Root}/Prefabs/{name}.prefab";
            var saved = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return saved;
        }

        private static Prefabs BuildPrefabs(Mats mats)
        {
            // --- floor tile: a square of card, lying flat -----------------------------
            var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tile.name = "FloorTile";
            tile.transform.localScale = new Vector3(CellSize * 0.98f, 0.0015f, CellSize * 0.98f);
            tile.GetComponent<Renderer>().sharedMaterial = mats.Cardboard;
            Object.DestroyImmediate(tile.GetComponent<Collider>());

            // --- wall piece: a slab of dungeon stone -----------------------------------
            // Kept a shade under figure height on purpose: tall enough to read as a wall from
            // a seated angle, short enough that the near wall never hides the party behind it.
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "WallPiece";
            wall.transform.localScale = new Vector3(CellSize, CellSize * 0.68f, 0.0035f);
            wall.GetComponent<Renderer>().sharedMaterial = mats.Stone;
            Object.DestroyImmediate(wall.GetComponent<Collider>());

            return new Prefabs
            {
                FloorTile  = SavePrefab(tile, "FloorTile"),
                WallPiece  = SavePrefab(wall, "WallPiece"),

                // Blank stays the fallback for ids we have no figure for.
                Standee    = BuildStandee("Standee", mats.Plastic, mats.Paper),

                Fighter    = BuildMini(new MiniSpec { Name = "MiniFighter",   Body = mats.Fighter,    Accent = mats.Steel, Shape = PrimitiveType.Cube,    Girth = 0.30f, Height = 1.00f, Kit = Kit.Sword         }, mats.Slate),
                Thief      = BuildMini(new MiniSpec { Name = "MiniThief",     Body = mats.Thief,      Accent = mats.Steel, Shape = PrimitiveType.Capsule, Girth = 0.21f, Height = 0.86f, Kit = Kit.Dagger        }, mats.Slate),
                Priest     = BuildMini(new MiniSpec { Name = "MiniPriest",    Body = mats.Priest,     Accent = mats.Gold,  Shape = PrimitiveType.Capsule, Girth = 0.27f, Height = 0.96f, Kit = Kit.Mitre         }, mats.Slate),
                MagicUser  = BuildMini(new MiniSpec { Name = "MiniMagicUser", Body = mats.MagicUser,  Accent = mats.CardboardDark, Shape = PrimitiveType.Capsule, Girth = 0.25f, Height = 0.94f, Kit = Kit.Staff }, mats.Slate),
                Bishop     = BuildMini(new MiniSpec { Name = "MiniBishop",    Body = mats.Bishop,     Accent = mats.Gold,  Shape = PrimitiveType.Capsule, Girth = 0.27f, Height = 0.98f, Kit = Kit.MitreAndStaff }, mats.Slate),
                StandeeFoe = BuildMini(new MiniSpec { Name = "MiniGoblin",    Body = mats.PlasticFoe, Accent = mats.Steel, Shape = PrimitiveType.Capsule, Girth = 0.23f, Height = 0.62f, Kit = Kit.Club          }, mats.Slate),
            };
        }

        /// <summary>
        /// A figure on a round base, pivot at the base so it sits on the tile. Built from
        /// primitives rather than art: the point is that six of these are distinguishable at
        /// 28mm, not that they are pretty.
        /// </summary>
        private static GameObject BuildMini(MiniSpec spec, Material baseMat)
        {
            var root = new GameObject(spec.Name);

            var total = PrimitiveHeight * spec.Height;
            var width = CellSize * spec.Girth;
            var plinthTop = 0.0024f;

            var plinth = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            plinth.name = "Base";
            plinth.transform.SetParent(root.transform, false);
            plinth.transform.localScale = new Vector3(CellSize * PlinthCells, 0.0012f, CellSize * PlinthCells);
            plinth.transform.localPosition = new Vector3(0f, 0.0012f, 0f);
            plinth.GetComponent<Renderer>().sharedMaterial = baseMat;
            Object.DestroyImmediate(plinth.GetComponent<Collider>());

            var headDiameter = total * 0.22f;
            var bodyHeight = total - headDiameter * 0.75f;

            var body = GameObject.CreatePrimitive(spec.Shape);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            // Capsules and cylinders are two units tall at scale 1; cubes are one.
            var yScale = spec.Shape == PrimitiveType.Cube ? bodyHeight : bodyHeight * 0.5f;
            body.transform.localScale = new Vector3(width, yScale, width * 0.7f);
            body.transform.localPosition = new Vector3(0f, plinthTop + bodyHeight * 0.5f, 0f);
            body.GetComponent<Renderer>().sharedMaterial = spec.Body;
            Object.DestroyImmediate(body.GetComponent<Collider>());

            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(root.transform, false);
            head.transform.localScale = Vector3.one * headDiameter;
            head.transform.localPosition = new Vector3(0f, plinthTop + bodyHeight + headDiameter * 0.25f, 0f);
            head.GetComponent<Renderer>().sharedMaterial = spec.Body == null ? baseMat : spec.Body;
            Object.DestroyImmediate(head.GetComponent<Collider>());

            AddKit(root, spec, total, width, plinthTop, bodyHeight, headDiameter);

            return SavePrefab(root, spec.Name);
        }

        private static void AddKit(GameObject root, MiniSpec spec, float total, float width,
                                   float plinthTop, float bodyHeight, float headDiameter)
        {
            if (spec.Kit == Kit.None) return;

            if (spec.Kit == Kit.Sword || spec.Kit == Kit.Dagger || spec.Kit == Kit.Club)
            {
                var length = spec.Kit == Kit.Sword ? total * 0.62f
                           : spec.Kit == Kit.Club ? total * 0.34f
                           : total * 0.24f;
                var thickness = spec.Kit == Kit.Club ? 0.0022f : 0.0012f;

                var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
                blade.name = spec.Kit.ToString();
                blade.transform.SetParent(root.transform, false);
                blade.transform.localScale = new Vector3(thickness, length, thickness * 2.2f);
                blade.transform.localPosition = new Vector3(width * 0.55f, plinthTop + bodyHeight * 0.62f, 0f);
                blade.transform.localRotation = Quaternion.Euler(0f, 0f, -14f);
                blade.GetComponent<Renderer>().sharedMaterial = spec.Accent;
                Object.DestroyImmediate(blade.GetComponent<Collider>());
            }

            if (spec.Kit == Kit.Staff || spec.Kit == Kit.MitreAndStaff)
            {
                var staff = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                staff.name = "Staff";
                staff.transform.SetParent(root.transform, false);
                staff.transform.localScale = new Vector3(0.0011f, total * 0.58f * 0.5f, 0.0011f);
                staff.transform.localPosition = new Vector3(width * 0.58f, plinthTop + total * 0.58f * 0.5f, 0f);
                staff.GetComponent<Renderer>().sharedMaterial = spec.Accent;
                Object.DestroyImmediate(staff.GetComponent<Collider>());
            }

            if (spec.Kit == Kit.Mitre || spec.Kit == Kit.MitreAndStaff)
            {
                var mitre = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                mitre.name = "Mitre";
                mitre.transform.SetParent(root.transform, false);
                mitre.transform.localScale = new Vector3(headDiameter * 0.72f, headDiameter * 0.55f, headDiameter * 0.72f);
                mitre.transform.localPosition = new Vector3(0f, plinthTop + bodyHeight + headDiameter * 0.95f, 0f);
                mitre.GetComponent<Renderer>().sharedMaterial = spec.Accent;
                Object.DestroyImmediate(mitre.GetComponent<Collider>());
            }
        }

        /// <summary>A figure on a round base. Pivot at the base so it sits on the tile.</summary>
        private static GameObject BuildStandee(string name, Material bodyMat, Material baseMat)
        {
            var root = new GameObject(name);

            var plinth = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            plinth.name = "Base";
            plinth.transform.SetParent(root.transform, false);
            plinth.transform.localScale = new Vector3(CellSize * PlinthCells, 0.0012f, CellSize * PlinthCells);
            plinth.transform.localPosition = new Vector3(0f, 0.0012f, 0f);
            plinth.GetComponent<Renderer>().sharedMaterial = baseMat;
            Object.DestroyImmediate(plinth.GetComponent<Collider>());

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Figure";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(CellSize * 0.5f, StandeeHeight, 0.0015f);
            body.transform.localPosition = new Vector3(0f, StandeeHeight * 0.5f, 0f);
            body.GetComponent<Renderer>().sharedMaterial = bodyMat;
            Object.DestroyImmediate(body.GetComponent<Collider>());

            return SavePrefab(root, name);
        }

        private static void BuildScene(Mats mats, Prefabs prefabs)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // --- the table ---------------------------------------------------------------
            var tableTop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tableTop.name = "TableTop";
            tableTop.transform.localScale = new Vector3(1.4f, 0.04f, 0.9f);  // a real 140x90cm table
            tableTop.transform.position = new Vector3(0f, 0.73f, 0f);        // real table height
            tableTop.GetComponent<Renderer>().sharedMaterial = mats.Table;

            // Origin of the dungeon grid: back-left corner of the play area.
            var origin = new GameObject("TableOrigin");
            origin.transform.position = new Vector3(-0.28f, 0.75f, 0.26f);

            // --- lighting: one lamp over the table, warm ---------------------------------
            var lampGo = new GameObject("DeskLamp");
            var lamp = lampGo.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.color = new Color(1f, 0.93f, 0.82f);
            // Was 3.5 over a 4m range, which clipped the middle of the table to pure white and
            // took the stone walls with it. A desk lamp lights a table, not a stadium.
            lamp.intensity = 0.85f;
            lamp.range = 1.9f;
            lamp.shadows = LightShadows.Soft;
            lampGo.transform.position = new Vector3(0.15f, 1.45f, 0.15f);

            var fillGo = new GameObject("RoomFill");
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.25f;
            fill.color = new Color(0.75f, 0.80f, 0.95f);
            fillGo.transform.rotation = Quaternion.Euler(50f, -140f, 0f);

            // Lifted a little now the lamp is dimmer: the falloff at the table edges should go
            // to shadow, not to black, or the outer corridors disappear entirely.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.24f, 0.25f, 0.29f);
            RenderSettings.ambientEquatorColor = new Color(0.18f, 0.18f, 0.19f);
            RenderSettings.ambientGroundColor = new Color(0.08f, 0.07f, 0.07f);

            // --- camera on a rig, so an XR rig can replace the rig and not the camera ----
            // TableCamera drives the rig and follows the party. Framing the whole tabletop puts
            // a 1-inch-per-cell dungeon at a few percent of frame width, so the seat has to be
            // close: the camera is left at the rig's origin and the rig does the moving.
            var rig = new GameObject("CameraRig");
            rig.transform.position = new Vector3(0f, 0.95f, -0.62f);

            var camGo = new GameObject("MainCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(rig.transform, false);
            camGo.transform.localPosition = Vector3.zero;
            camGo.transform.localRotation = Quaternion.identity;
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.01f;   // we sit ~34cm from the minis now
            camGo.AddComponent<AudioListener>();

            // --- the DM's caption card, lying on the table (world-space: VR-safe) --------
            // Guarded: a fresh project may not have TMP Essentials imported yet, and that
            // must not cost us the rest of the scene.
            DmSubtitle subtitle = null;
            try
            {
                var cardGo = new GameObject("DmSubtitle");
                cardGo.transform.position = new Vector3(0f, 0.755f, -0.34f);
                cardGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                var text = cardGo.AddComponent<TextMeshPro>();
                text.text = string.Empty;
                text.fontSize = 0.6f;
                text.alignment = TextAlignmentOptions.Center;
                text.color = new Color(0.95f, 0.94f, 0.90f, 0f);
                text.rectTransform.sizeDelta = new Vector2(0.9f, 0.12f);
                subtitle = cardGo.AddComponent<DmSubtitle>();
                SetPrivate(subtitle, "label", text);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[setup] subtitle card skipped ({ex.Message}). " +
                                 "Window > TextMeshPro > Import TMP Essential Resources, then re-run.");
            }

            // --- the wiring ---------------------------------------------------------------
            var tableGo = new GameObject("Table");
            var renderer = tableGo.AddComponent<TableRenderer>();
            SetPrivate(renderer, "cellSize", CellSize);
            SetPrivate(renderer, "tableOrigin", origin.transform);
            SetPrivate(renderer, "floorTilePrefab", prefabs.FloorTile);
            SetPrivate(renderer, "wallPiecePrefab", prefabs.WallPiece);
            SetPrivate(renderer, "blankStandeePrefab", prefabs.Standee);
            // Keyed by ClassId for the party and MonsterId for foes — the ids the snapshot uses.
            SetPrivate(renderer, "standees", new[]
            {
                new TableRenderer.StandeeEntry { id = "Fighter",   prefab = prefabs.Fighter },
                new TableRenderer.StandeeEntry { id = "Thief",     prefab = prefabs.Thief },
                new TableRenderer.StandeeEntry { id = "Priest",    prefab = prefabs.Priest },
                new TableRenderer.StandeeEntry { id = "MagicUser", prefab = prefabs.MagicUser },
                new TableRenderer.StandeeEntry { id = "Bishop",    prefab = prefabs.Bishop },
                new TableRenderer.StandeeEntry { id = "Goblin",    prefab = prefabs.StandeeFoe },
            });
            // Vocabulary bridge. The figures are carved for Wizardry's classes, but a driving game
            // may speak AD&D (Cleric, Paladin, Halfling). Race_Class aliases come first so a race
            // with its own sculpt keeps it; the bare-class ones are the catch-all beneath.
            SetPrivate(renderer, "standeeAliases", new[]
            {
                // Race spellings: Wizardry's Hobbit is AD&D's Halfling.
                new TableRenderer.AliasEntry { from = "Halfling_Thief",     to = "Hobbit_Thief" },
                new TableRenderer.AliasEntry { from = "Halfling_MagicUser", to = "Gnome_Bishop" },

                // Reach the sculpted figure for that race rather than a bare-class primitive.
                new TableRenderer.AliasEntry { from = "Human_Cleric",       to = "Human_Priest" },
                new TableRenderer.AliasEntry { from = "Gnome_MagicUser",    to = "Gnome_Bishop" },
                new TableRenderer.AliasEntry { from = "Gnome_Cleric",       to = "Human_Priest" },
                new TableRenderer.AliasEntry { from = "Elf_Cleric",         to = "Human_Priest" },
                new TableRenderer.AliasEntry { from = "Dwarf_Cleric",       to = "Human_Priest" },

                // Classes with no figure of their own borrow the nearest fit.
                new TableRenderer.AliasEntry { from = "Cleric",      to = "Priest" },
                new TableRenderer.AliasEntry { from = "Druid",       to = "Priest" },
                new TableRenderer.AliasEntry { from = "Paladin",     to = "Fighter" },
                new TableRenderer.AliasEntry { from = "Ranger",      to = "Fighter" },
                new TableRenderer.AliasEntry { from = "Monk",        to = "Fighter" },
                new TableRenderer.AliasEntry { from = "Illusionist", to = "MagicUser" },
                new TableRenderer.AliasEntry { from = "Assassin",    to = "Thief" },
                new TableRenderer.AliasEntry { from = "Bard",        to = "Thief" },
            });

            SetPrivate(renderer, "revealEntireLevel", true);

            // Wired explicitly rather than left to field defaults: these two decide whether the
            // party clears the corridor walls, and MiniFromStl sizes imported figures against them.
            SetPrivate(renderer, "fileSpacing", 0.27f);
            SetPrivate(renderer, "rankSpacing", 0.28f);

            // Wired after the table exists, since the camera follows what the table places.
            var tableCam = rig.AddComponent<TableCamera>();
            SetPrivate(tableCam, "table", renderer);

            var viewerGo = new GameObject("ViewerReceiver");
            var viewer = viewerGo.AddComponent<ViewerReceiver>();
            SetPrivate(viewer, "table", renderer);
            SetPrivate(viewer, "subtitle", subtitle);

            // --- the choice cards, laid beside the party ----------------------------------
            // Rebuilt here rather than left to be added by hand, because this menu item recreates the
            // scene from scratch: anything only ever wired manually is silently lost the next time it
            // runs, and the symptom is a table that simply stops offering choices.
            //
            // The font comes off the subtitle's label, so a project without TMP Essentials imported
            // ends up with no font here either -- and TablePrompt says so rather than drawing blanks.
            var promptGo = new GameObject("TablePrompt");
            var tablePrompt = promptGo.AddComponent<TablePrompt>();
            SetPrivate(tablePrompt, "receiver", viewer);
            SetPrivate(tablePrompt, "table", renderer);
            SetPrivate(tablePrompt, "cellSize", CellSize);
            SetPrivate(tablePrompt, "panelMaterial", mats.Glass);
            SetPrivate(tablePrompt, "buttonMaterial", mats.GlassButton);
            SetPrivate(tablePrompt, "buttonHotMaterial", mats.GlassButtonHot);

            if (subtitle != null)
            {
                var subtitleLabel = subtitle.GetComponent<TMP_Text>();
                if (subtitleLabel != null) SetPrivate(tablePrompt, "font", subtitleLabel.font);
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.OpenScene(ScenePath);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        /// <summary>Assign a [SerializeField] private without making it public just for setup.</summary>
        private static void SetPrivate(Object target, string field, object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogWarning($"[setup] no serialized field '{field}' on {target.GetType().Name}");
                return;
            }

            switch (value)
            {
                case float f: prop.floatValue = f; break;
                case bool b: prop.boolValue = b; break;
                case int i2: prop.intValue = i2; break;
                case Object o: prop.objectReferenceValue = o; break;
                case TableRenderer.StandeeEntry[] entries:
                    prop.arraySize = entries.Length;
                    for (int i = 0; i < entries.Length; i++)
                    {
                        var el = prop.GetArrayElementAtIndex(i);
                        el.FindPropertyRelative("id").stringValue = entries[i].id;
                        el.FindPropertyRelative("prefab").objectReferenceValue = entries[i].prefab;
                    }
                    break;
                case TableRenderer.AliasEntry[] aliases:
                    prop.arraySize = aliases.Length;
                    for (int i = 0; i < aliases.Length; i++)
                    {
                        var el = prop.GetArrayElementAtIndex(i);
                        el.FindPropertyRelative("from").stringValue = aliases[i].from;
                        el.FindPropertyRelative("to").stringValue = aliases[i].to;
                    }
                    break;
                default:
                    // Silence here cost us a wrongly-defaulted field once already: an unhandled
                    // type used to fall straight through and leave the field untouched.
                    Debug.LogWarning($"[setup] don't know how to assign {value?.GetType().Name ?? "null"} " +
                                     $"to '{field}' — field left at its default");
                    break;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
