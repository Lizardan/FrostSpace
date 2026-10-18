using System.Collections.Generic;
using FrostSpace.Features.Locomotion;
using FrostSpace.Features.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FrostSpace.Editor
{
    /// <summary>
    /// Собирает снежный уровень FrostSpace целиком скриптом: землю, дом,
    /// деревья, бочки, свет, туман, камеру сверху, игрока и NPC-роботов.
    /// Повторный запуск пересоздаёт сцену с нуля, поэтому уровень
    /// воспроизводим, а не собран мышью.
    /// </summary>
    public static class SnowLevelBuilder
    {
        public const string ScenePath = "Assets/Scenes/SnowLevel.unity";

        private const string MaterialFolder = "Assets/_Project/Content/Materials/Level";
        private const string PrefabFolder = "Assets/_Project/Content/Prefabs/Robots";

        private static readonly string[] NpcRobots =
        {
            "R2_Furnace", "R3_Scout", "R4_Hauler", "R5_Ranger",
        };

        private static readonly Vector3[] NpcPositions =
        {
            new(-9f, 0f, 3f), new(13f, 0f, -3f), new(8f, 0f, 13f), new(-5f, 0f, 11f),
        };

        private static readonly float[] NpcYaw = { 150f, 210f, 40f, 320f };

        [MenuItem("FrostSpace/Level/Build Snow Level")]
        public static void Build()
        {
            EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var materials = BuildMaterials();
            ConfigureLighting(materials);
            BuildTerrain(materials);
            BuildHouse(materials);
            BuildTrees(materials);
            BuildProps(materials);
            var player = BuildPlayer();
            BuildNpcs();
            BuildCamera(player);
            BuildSnowfall(materials);

            EnsureFolder(System.IO.Path.GetDirectoryName(ScenePath)?.Replace('\\', '/'));
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings();
            Debug.Log("SnowLevelBuilder: уровень сохранён в " + ScenePath);
        }

        // ------------------------------------------------------------- материалы

        private sealed class Palette
        {
            public Material Snow;
            public Material SnowDeep;
            public Material Wood;
            public Material WoodDark;
            public Material Roof;
            public Material Pine;
            public Material PineSnow;
            public Material Metal;
            public Material Rust;
            public Material Orange;
            public Material Stone;
            public Material Window;
        }

        private static Palette BuildMaterials()
        {
            EnsureFolder(MaterialFolder);
            return new Palette
            {
                Snow = Lit("Level_Snow", "#E4EDF6", 0.18f),
                SnowDeep = Lit("Level_SnowDeep", "#C9D8E6", 0.22f),
                Wood = Lit("Level_Wood", "#7A5334", 0.28f),
                WoodDark = Lit("Level_WoodDark", "#432E20", 0.32f),
                Roof = Lit("Level_Roof", "#5A4034", 0.3f),
                Pine = Lit("Level_Pine", "#244534", 0.35f),
                PineSnow = Lit("Level_PineSnow", "#DCE7F1", 0.22f),
                Metal = Lit("Level_Metal", "#98A3AF", 0.45f, 0.85f),
                Rust = Lit("Level_Rust", "#8C4A32", 0.7f, 0.2f),
                Orange = Lit("Level_Orange", "#E08A2E", 0.4f),
                Stone = Lit("Level_Stone", "#4C5158", 0.6f),
                Window = Lit("Level_Window", "#FFC978", 0.35f, 0f, "#FFB55C", 2.2f),
            };
        }

        private static Material Lit(
            string name, string hex, float smoothness, float metallic = 0f,
            string emissionHex = null, float emissionStrength = 1f)
        {
            var path = MaterialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }

            ColorUtility.TryParseHtmlString(hex, out var color);
            material.SetColor("_BaseColor", color.linear);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);

            if (!string.IsNullOrEmpty(emissionHex))
            {
                ColorUtility.TryParseHtmlString(emissionHex, out var emission);
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                material.SetColor("_EmissionColor", emission.linear * emissionStrength);
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        // -------------------------------------------------------------- сцена

        private static void ConfigureLighting(Palette palette)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.78f, 0.85f, 0.92f);
            RenderSettings.fogStartDistance = 22f;
            RenderSettings.fogEndDistance = 95f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.72f, 0.80f, 0.90f);
            RenderSettings.ambientEquatorColor = new Color(0.52f, 0.58f, 0.66f);
            RenderSettings.ambientGroundColor = new Color(0.30f, 0.34f, 0.40f);
            RenderSettings.skybox = BuildSkybox();

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.95f, 0.86f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.72f;
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);

            var lamp = new GameObject("HouseLamp").AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.color = new Color(1f, 0.78f, 0.45f);
            lamp.intensity = 2.4f;
            lamp.range = 14f;
            lamp.transform.position = new Vector3(12f, 2.6f, 8f);
            lamp.shadows = LightShadows.None;
        }

        private static Material BuildSkybox()
        {
            var path = MaterialFolder + "/Level_Sky.mat";
            var skybox = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Skybox/Procedural");
            if (shader == null)
                return skybox;

            if (skybox == null)
            {
                skybox = new Material(shader);
                AssetDatabase.CreateAsset(skybox, path);
            }

            skybox.shader = shader;
            skybox.SetColor("_SkyTint", new Color(0.62f, 0.72f, 0.85f));
            skybox.SetColor("_GroundColor", new Color(0.72f, 0.78f, 0.84f));
            skybox.SetColor("_SunColor", new Color(1f, 0.95f, 0.85f));
            skybox.SetFloat("_AtmosphereThickness", 1.4f);
            skybox.SetFloat("_Exposure", 1.15f);
            EditorUtility.SetDirty(skybox);
            return skybox;
        }

        private static void BuildTerrain(Palette palette)
        {
            var root = new GameObject("Terrain").transform;

            var ground = CreatePrimitive(PrimitiveType.Plane, "SnowGround", root);
            ground.transform.localScale = new Vector3(9f, 1f, 9f);
            Paint(ground, palette.Snow);

            var rnd = new System.Random(7);
            for (var i = 0; i < 14; i++)
            {
                var angle = (float)(rnd.NextDouble() * Mathf.PI * 2f);
                var radius = 16f + (float)rnd.NextDouble() * 20f;
                var drift = CreatePrimitive(PrimitiveType.Sphere, "SnowDrift", root);
                drift.transform.position = new Vector3(
                    Mathf.Cos(angle) * radius, -0.12f, Mathf.Sin(angle) * radius);
                drift.transform.localScale = new Vector3(
                    2.4f + (float)rnd.NextDouble() * 2.6f, 0.9f,
                    2.4f + (float)rnd.NextDouble() * 2.6f);
                Paint(drift, palette.SnowDeep);
                Object.DestroyImmediate(drift.GetComponent<Collider>());
            }
        }

        private static void BuildHouse(Palette palette)
        {
            var root = new GameObject("House").transform;
            root.position = new Vector3(12f, 0f, 8f);
            root.rotation = Quaternion.Euler(0f, -22f, 0f);

            var foundation = CreatePrimitive(PrimitiveType.Cube, "Foundation", root);
            foundation.transform.localPosition = new Vector3(0f, 0.2f, 0f);
            foundation.transform.localScale = new Vector3(7.6f, 0.4f, 6.0f);
            Paint(foundation, palette.Stone);

            var body = CreatePrimitive(PrimitiveType.Cube, "Body", root);
            body.transform.localPosition = new Vector3(0f, 1.75f, 0f);
            body.transform.localScale = new Vector3(7f, 3.1f, 5.5f);
            Paint(body, palette.Wood);

            for (var i = 0; i < 4; i++)
            {
                var belt = CreatePrimitive(PrimitiveType.Cube, "Beam", root);
                belt.transform.localPosition = new Vector3(
                    i < 2 ? -3.5f : 3.5f, 1.75f, i % 2 == 0 ? -2.72f : 2.72f);
                belt.transform.localScale = new Vector3(0.28f, 3.1f, 0.3f);
                Paint(belt, palette.WoodDark);
            }

            for (var side = -1; side <= 1; side += 2)
            {
                var slab = CreatePrimitive(PrimitiveType.Cube, "RoofSlab", root);
                slab.transform.localPosition = new Vector3(0f, 4.05f, side * 1.45f);
                slab.transform.localRotation = Quaternion.Euler(side * 33f, 0f, 0f);
                slab.transform.localScale = new Vector3(7.8f, 0.35f, 3.5f);
                Paint(slab, palette.Roof);

                var snow = CreatePrimitive(PrimitiveType.Cube, "RoofSnow", root);
                snow.transform.localPosition = new Vector3(0f, 4.24f, side * 1.42f);
                snow.transform.localRotation = Quaternion.Euler(side * 33f, 0f, 0f);
                snow.transform.localScale = new Vector3(7.9f, 0.16f, 3.5f);
                Paint(snow, palette.Snow);
                Object.DestroyImmediate(snow.GetComponent<Collider>());
            }

            var chimney = CreatePrimitive(PrimitiveType.Cube, "Chimney", root);
            chimney.transform.localPosition = new Vector3(2.1f, 5.0f, 1.1f);
            chimney.transform.localScale = new Vector3(0.9f, 2.4f, 0.9f);
            Paint(chimney, palette.Stone);

            var chimneyCap = CreatePrimitive(PrimitiveType.Cube, "ChimneySnow", root);
            chimneyCap.transform.localPosition = new Vector3(2.1f, 6.25f, 1.1f);
            chimneyCap.transform.localScale = new Vector3(1.05f, 0.14f, 1.05f);
            Paint(chimneyCap, palette.Snow);
            Object.DestroyImmediate(chimneyCap.GetComponent<Collider>());

            var door = CreatePrimitive(PrimitiveType.Cube, "Door", root);
            door.transform.localPosition = new Vector3(-1.4f, 1.2f, -2.8f);
            door.transform.localScale = new Vector3(1.3f, 2.2f, 0.18f);
            Paint(door, palette.WoodDark);

            var handle = CreatePrimitive(PrimitiveType.Cube, "DoorHandle", root);
            handle.transform.localPosition = new Vector3(-0.9f, 1.25f, -2.92f);
            handle.transform.localScale = new Vector3(0.12f, 0.12f, 0.1f);
            Paint(handle, palette.Orange);

            foreach (var x in new[] { -3.1f, 3.1f })
            {
                var window = CreatePrimitive(PrimitiveType.Cube, "Window", root);
                window.transform.localPosition = new Vector3(x, 2.0f, -2.8f);
                window.transform.localScale = new Vector3(0.34f, 2.6f, 0.14f);
                Paint(window, palette.Window);
            }

            var windowFront = CreatePrimitive(PrimitiveType.Cube, "WindowFront", root);
            windowFront.transform.localPosition = new Vector3(1.2f, 2.05f, -2.82f);
            windowFront.transform.localScale = new Vector3(1.5f, 1.2f, 0.12f);
            Paint(windowFront, palette.Window);

            for (var side = -1; side <= 1; side += 2)
            {
                var post = CreatePrimitive(PrimitiveType.Cube, "PorchPost", root);
                post.transform.localPosition = new Vector3(side * 2.6f, 1.4f, -3.4f);
                post.transform.localScale = new Vector3(0.22f, 2.8f, 0.22f);
                Paint(post, palette.Wood);

                var postSnow = CreatePrimitive(PrimitiveType.Cube, "PorchSnow", root);
                postSnow.transform.localPosition = new Vector3(side * 2.6f, 2.85f, -3.4f);
                postSnow.transform.localScale = new Vector3(0.3f, 0.1f, 0.3f);
                Paint(postSnow, palette.Snow);
                Object.DestroyImmediate(postSnow.GetComponent<Collider>());
            }

            var porchRoof = CreatePrimitive(PrimitiveType.Cube, "PorchRoof", root);
            porchRoof.transform.localPosition = new Vector3(0f, 3.0f, -3.35f);
            porchRoof.transform.localRotation = Quaternion.Euler(12f, 0f, 0f);
            porchRoof.transform.localScale = new Vector3(6.0f, 0.22f, 1.5f);
            Paint(porchRoof, palette.Roof);
        }

        private static void BuildTrees(Palette palette)
        {
            var root = new GameObject("Trees").transform;
            var positions = new[]
            {
                new Vector3(-18f, 0f, 8f), new Vector3(-14f, 0f, 18f),
                new Vector3(-6f, 0f, 20f), new Vector3(4f, 0f, 22f),
                new Vector3(18f, 0f, 18f), new Vector3(24f, 0f, 6f),
                new Vector3(22f, 0f, -8f), new Vector3(8f, 0f, -18f),
                new Vector3(-8f, 0f, -16f), new Vector3(-20f, 0f, -8f),
                new Vector3(-22f, 0f, 2f), new Vector3(0f, 0f, 16f),
                new Vector3(6f, 0f, 3f), new Vector3(-7f, 0f, -3f),
                new Vector3(16f, 0f, 15f), new Vector3(-13f, 0f, 15f),
            };

            var rnd = new System.Random(11);
            for (var i = 0; i < positions.Length; i++)
            {
                var tree = new GameObject("Pine_" + i).transform;
                tree.SetParent(root, false);
                tree.position = positions[i];
                tree.localScale = Vector3.one * (0.85f + (float)rnd.NextDouble() * 0.4f);

                var trunk = CreatePrimitive(PrimitiveType.Cylinder, "Trunk", tree);
                trunk.transform.localPosition = new Vector3(0f, 0.8f, 0f);
                trunk.transform.localScale = new Vector3(0.42f, 0.8f, 0.42f);
                Paint(trunk, palette.WoodDark);

                for (var tier = 0; tier < 3; tier++)
                {
                    var height = 2.4f + tier * 1.5f;
                    var radius = 2.1f - tier * 0.45f;
                    var cone = CreateCone("Crown", tree);
                    cone.transform.localPosition = new Vector3(0f, height, 0f);
                    cone.transform.localScale = new Vector3(radius, 1.0f, radius);
                    Paint(cone, palette.Pine);

                    var snowCone = CreateCone("CrownSnow", tree);
                    snowCone.transform.localPosition = new Vector3(0f, height + 0.62f, 0f);
                    snowCone.transform.localScale = new Vector3(radius * 0.62f, 0.34f, radius * 0.62f);
                    Paint(snowCone, palette.PineSnow);
                    Object.DestroyImmediate(snowCone.GetComponent<Collider>());
                }
            }
        }

        private static void BuildProps(Palette palette)
        {
            var root = new GameObject("Props").transform;
            var barrels = new[]
            {
                new Vector3(6f, 0f, 5f), new Vector3(7.1f, 0f, 5.6f),
                new Vector3(5.2f, 0f, 6.4f), new Vector3(15f, 0f, 1.4f),
                new Vector3(16f, 0f, 2.6f), new Vector3(-3f, 0f, 7f),
            };

            for (var i = 0; i < barrels.Length; i++)
            {
                var barrel = CreatePrimitive(PrimitiveType.Cylinder, "Barrel_" + i, root);
                barrel.transform.position = barrels[i] + Vector3.up * 0.5f;
                barrel.transform.localScale = new Vector3(0.9f, 0.5f, 0.9f);
                Paint(barrel, i % 2 == 0 ? palette.Rust : palette.Metal);

                for (var ring = 0; ring < 2; ring++)
                {
                    var band = CreatePrimitive(PrimitiveType.Cylinder, "Band", barrel.transform);
                    band.transform.localPosition = new Vector3(0f, ring == 0 ? -0.22f : 0.22f, 0f);
                    band.transform.localScale = new Vector3(1.06f, 0.06f, 1.06f);
                    Paint(band, palette.Orange);
                }
            }

            var crates = new[]
            {
                new Vector3(9.5f, 0f, 4.2f), new Vector3(10.4f, 0f, 4.8f),
                new Vector3(10f, 0.62f, 4.5f), new Vector3(2.5f, 0f, 10f),
            };

            for (var i = 0; i < crates.Length; i++)
            {
                var crate = CreatePrimitive(PrimitiveType.Cube, "Crate_" + i, root);
                crate.transform.position = crates[i] + Vector3.up * 0.31f;
                crate.transform.rotation = Quaternion.Euler(0f, i * 24f, 0f);
                crate.transform.localScale = new Vector3(0.62f, 0.62f, 0.62f);
                Paint(crate, palette.Wood);
            }

            var rocks = new[]
            {
                new Vector3(-4f, 0f, -5f), new Vector3(4f, 0f, -9f),
                new Vector3(19f, 0f, 9f), new Vector3(-11f, 0f, -2f),
                new Vector3(2f, 0f, 14f),
            };

            for (var i = 0; i < rocks.Length; i++)
            {
                var rock = CreatePrimitive(PrimitiveType.Sphere, "Rock_" + i, root);
                rock.transform.position = rocks[i] + Vector3.up * 0.18f;
                rock.transform.localScale = new Vector3(
                    1.2f + i * 0.15f, 0.6f, 1.0f + i * 0.12f);
                rock.transform.rotation = Quaternion.Euler(0f, i * 40f, 0f);
                Paint(rock, palette.Stone);

                var cap = CreatePrimitive(PrimitiveType.Sphere, "RockSnow_" + i, root);
                cap.transform.position = rock.transform.position + Vector3.up * 0.26f;
                cap.transform.localScale = new Vector3(
                    1.05f + i * 0.13f, 0.28f, 0.9f + i * 0.1f);
                Paint(cap, palette.Snow);
                Object.DestroyImmediate(cap.GetComponent<Collider>());
            }

            var fence = new GameObject("Fence").transform;
            fence.SetParent(root, false);
            for (var i = 0; i < 9; i++)
            {
                var post = CreatePrimitive(PrimitiveType.Cube, "FencePost", fence);
                post.transform.position = new Vector3(-1f + i * 2.2f, 0.6f, -1.5f);
                post.transform.localScale = new Vector3(0.18f, 1.2f, 0.18f);
                Paint(post, palette.WoodDark);
            }

            for (var rail = 0; rail < 2; rail++)
            {
                var beam = CreatePrimitive(PrimitiveType.Cube, "FenceRail", fence);
                beam.transform.position = new Vector3(7.8f, 0.55f + rail * 0.5f, -1.5f);
                beam.transform.localScale = new Vector3(18.4f, 0.12f, 0.12f);
                Paint(beam, palette.Wood);
            }
        }

        // ------------------------------------------------------------- роботы

        private static GameObject BuildPlayer()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                PrefabFolder + "/R1_PolarMechanic.prefab");
            var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            player.name = "Player";
            player.transform.position = new Vector3(0f, 0.2f, -8f);
            player.transform.rotation = Quaternion.Euler(0f, 20f, 0f);
            return player;
        }

        private static void BuildNpcs()
        {
            var root = new GameObject("NPC").transform;
            for (var i = 0; i < NpcRobots.Length; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    PrefabFolder + "/" + NpcRobots[i] + ".prefab");
                var npc = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                npc.name = NpcRobots[i];
                npc.transform.SetParent(root, false);
                npc.transform.position = NpcPositions[i];
                npc.transform.rotation = Quaternion.Euler(0f, NpcYaw[i], 0f);

                var controller = npc.GetComponent<CharacterController>();
                if (controller != null)
                    controller.enabled = false;

                var animator = npc.GetComponentInChildren<Animator>();
                if (animator != null)
                    animator.Play("Idle", 0, (i * 0.37f) % 1f);
            }
        }

        private static void BuildCamera(GameObject player)
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 52f;
            camera.nearClipPlane = 0.15f;
            camera.farClipPlane = 220f;
            camera.clearFlags = CameraClearFlags.Skybox;
            cameraObject.AddComponent<AudioListener>();

            var follow = cameraObject.AddComponent<TopDownCamera>();
            follow.SetTarget(player.transform);
            cameraObject.transform.position = player.transform.position + new Vector3(0f, 11f, -9f);
            cameraObject.transform.LookAt(player.transform.position + Vector3.up);

            var locomotion = player.GetComponent<RobotLocomotion>();
            if (locomotion != null)
            {
                var serialized = new SerializedObject(locomotion);
                serialized.FindProperty("_camera").objectReferenceValue = cameraObject.transform;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void BuildSnowfall(Palette palette)
        {
            var snow = new GameObject("Snowfall");
            snow.transform.position = new Vector3(0f, 14f, 4f);
            var system = snow.AddComponent<ParticleSystem>();
            system.Stop();

            var main = system.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(7f, 12f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.13f);
            main.startColor = new Color(1f, 1f, 1f, 0.85f);
            main.gravityModifier = 0.06f;
            main.maxParticles = 900;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = system.emission;
            emission.rateOverTime = 130f;

            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(56f, 1f, 56f);

            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            velocity.y = new ParticleSystem.MinMaxCurve(-0.4f, -0.1f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);

            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(new Color(0.86f, 0.92f, 1f), 1f),
                },
                new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0f, 1f) });
            var colorOverLifetime = system.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = gradient;

            var path = MaterialFolder + "/Level_Snowfall.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                             ?? Shader.Find("Particles/Standard Unlit")
                             ?? Shader.Find("Sprites/Default");
                material = new Material(shader);
                material.SetColor("_BaseColor", Color.white);
                material.SetColor("_Color", Color.white);
                AssetDatabase.CreateAsset(material, path);
            }

            var renderer = snow.GetComponent<ParticleSystemRenderer>();
            renderer.material = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;

            system.Play();
        }

        // -------------------------------------------------------------- утилиты

        private static GameObject CreatePrimitive(
            PrimitiveType type, string name, Transform parent)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            return go;
        }

        /// <summary>Гранёный конус радиусом 1 и высотой 2 — вместо отсутствующего примитива.</summary>
        private static GameObject CreateCone(string name, Transform parent, int segments = 8)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var apex = new Vector3(0f, 1f, 0f);

            for (var i = 0; i < segments; i++)
            {
                var a0 = Mathf.PI * 2f * i / segments;
                var a1 = Mathf.PI * 2f * (i + 1) / segments;
                var b = new Vector3(Mathf.Cos(a0), -1f, Mathf.Sin(a0));
                var c = new Vector3(Mathf.Cos(a1), -1f, Mathf.Sin(a1));

                var start = vertices.Count;
                vertices.Add(apex);
                vertices.Add(b);
                vertices.Add(c);
                triangles.Add(start);
                triangles.Add(start + 1);
                triangles.Add(start + 2);

                start = vertices.Count;
                vertices.Add(Vector3.down);
                vertices.Add(c);
                vertices.Add(b);
                triangles.Add(start);
                triangles.Add(start + 1);
                triangles.Add(start + 2);
            }

            var mesh = new Mesh { name = "Cone" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>();
            var collider = go.AddComponent<MeshCollider>();
            collider.convex = true;
            return go;
        }

        private static void Paint(GameObject go, Material material)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = material;
        }

        private static void AddToBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(scene => scene.path == ScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path))
                return;

            var parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            var name = System.IO.Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(name))
            {
                EnsureFolder(parent);
                AssetDatabase.CreateFolder(parent, name);
            }
        }
    }
}
