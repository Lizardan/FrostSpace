// Единый пресет графики FrostSpace: один уровень качества, один URP-ассет,
// один рендерер. Запускается из Tools/Unity/run_snippet.py (см. docs/DEVELOPMENT.md).
//
// Значения подобраны под WebGL2 как самую слабую цель: там нет compute-шейдеров,
// поэтому рендерер обязан быть Forward (Forward+ не работает), а дорогие
// полноэкранные копии (depth/opaque texture) выключены.
//
// Сниппет идемпотентен: повторный запуск не ломает уже применённое состояние.
// Правила сниппета (docs/UNITY_EDITOR.md §2): без using, полные имена, return string.

var log = new System.Text.StringBuilder();
void Say(string s) { log.AppendLine(s); }

string RP_PATH_OLD = "Assets/Settings/PC_RPAsset.asset";
string RENDERER_PATH_OLD = "Assets/Settings/PC_Renderer.asset";
string RP_NAME = "FrostSpace_RPAsset";
string RENDERER_NAME = "FrostSpace_Renderer";

// ---------- 1. Переименование PC_* в нейтральные имена ----------
string rpPath = UnityEditor.AssetDatabase.GetAssetPath(
    UnityEditor.AssetDatabase.LoadMainAssetAtPath(RP_PATH_OLD)) == "" ? null : RP_PATH_OLD;
string rendererPath = RENDERER_PATH_OLD;

if (rpPath != null)
{
    string err = UnityEditor.AssetDatabase.RenameAsset(RP_PATH_OLD, RP_NAME);
    Say("rename RP: " + (err == "" ? "ok -> Assets/Settings/" + RP_NAME + ".asset" : "ERR " + err));
}
else
{
    Say("rename RP: уже переименован");
}

if (UnityEditor.AssetDatabase.LoadMainAssetAtPath(RENDERER_PATH_OLD) != null)
{
    string err = UnityEditor.AssetDatabase.RenameAsset(RENDERER_PATH_OLD, RENDERER_NAME);
    Say("rename renderer: " + (err == "" ? "ok -> Assets/Settings/" + RENDERER_NAME + ".asset" : "ERR " + err));
}
else
{
    Say("rename renderer: уже переименован");
}

string rpFinal = "Assets/Settings/" + RP_NAME + ".asset";
string rendererFinal = "Assets/Settings/" + RENDERER_NAME + ".asset";

var urp = UnityEditor.AssetDatabase.LoadAssetAtPath<
    UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>(rpFinal);
if (urp == null) { return "FAIL: не найден " + rpFinal; }

// ---------- 2. URP-ассет ----------
var urpSo = new UnityEditor.SerializedObject(urp);

// Типы полей в URP-ассете неоднородны (часть «целочисленных» настроек на самом деле
// float). Пишем по фактическому propertyType, иначе set_intValue кидает
// "type is not a supported int value" и валит ошибку в Console.
void SetInt(string p, int v)
{
    var prop = urpSo.FindProperty(p);
    if (prop == null) { Say("URP: НЕТ СВОЙСТВА " + p); return; }
    if (prop.propertyType == UnityEditor.SerializedPropertyType.Float) prop.floatValue = v;
    else if (prop.propertyType == UnityEditor.SerializedPropertyType.Integer
          || prop.propertyType == UnityEditor.SerializedPropertyType.Enum) prop.intValue = v;
    else Say("URP: НЕОЖИДАННЫЙ ТИП " + p + " (" + prop.propertyType + ")");
}
void SetBool(string p, bool v)
{
    var prop = urpSo.FindProperty(p);
    if (prop == null) { Say("URP: НЕТ СВОЙСТВА " + p); return; }
    if (prop.propertyType != UnityEditor.SerializedPropertyType.Boolean)
    {
        Say("URP: НЕОЖИДАННЫЙ ТИП " + p + " (" + prop.propertyType + ")");
        return;
    }
    prop.boolValue = v;
}
void SetFloat(string p, float v)
{
    var prop = urpSo.FindProperty(p);
    if (prop == null) { Say("URP: НЕТ СВОЙСТВА " + p); return; }
    if (prop.propertyType == UnityEditor.SerializedPropertyType.Integer) prop.intValue = (int)v;
    else if (prop.propertyType == UnityEditor.SerializedPropertyType.Float) prop.floatValue = v;
    else Say("URP: НЕОЖИДАННЫЙ ТИП " + p + " (" + prop.propertyType + ")");
}

// Текстуры глубины/цвета никому не нужны: Volume в сцене нет, SSAO выключен.
SetBool("m_RequireDepthTexture", false);
SetBool("m_RequireOpaqueTexture", false);
SetInt("m_OpaqueDownsampling", 1);
SetBool("m_SupportsTerrainHoles", false);

SetBool("m_SupportsHDR", true);
SetInt("m_HDRColorBufferPrecision", 0);   // 32 бита, дешевле 64
SetInt("m_MSAA", 1);                      // Disabled: сглаживание даёт SMAA на камере
SetFloat("m_RenderScale", 1.0f);
SetInt("m_ShEvalMode", 0);                // Auto

SetInt("m_MainLightRenderingMode", 1);    // PerPixel
SetBool("m_MainLightShadowsSupported", true);
SetInt("m_MainLightShadowmapResolution", 2048);
SetBool("m_MainLightShadowsSupported", true);

SetInt("m_AdditionalLightsRenderingMode", 1);  // PerPixel
SetInt("m_AdditionalLightsPerObjectLimit", 4);
SetBool("m_AdditionalLightShadowsSupported", true);   // прожектор у крыльца
SetInt("m_AdditionalLightsShadowmapResolution", 2048);

SetFloat("m_ShadowDistance", 50.0f);
SetInt("m_ShadowCascadeCount", 4);
SetFloat("m_CascadeBorder", 0.1f);
SetFloat("m_ShadowDepthBias", 0.1f);
SetFloat("m_ShadowNormalBias", 0.5f);
SetBool("m_SoftShadowsSupported", true);
SetInt("m_SoftShadowQuality", 2);         // Medium = 5x5 tent: ощутимо дешевле 7x7
SetBool("m_ConservativeEnclosingSphere", true);
SetInt("m_NumIterationsEnclosingSphere", 64);

SetBool("m_UseSRPBatcher", true);
SetBool("m_SupportsDynamicBatching", false);
SetBool("m_MixedLightingSupported", true);
SetBool("m_SupportsLightLayers", true);

SetBool("m_UseAdaptivePerformance", false);
SetInt("m_ColorGradingMode", 1);          // HDR
SetInt("m_ColorGradingLutSize", 32);
SetBool("m_UseFastSRGBLinearConversion", true);
SetBool("m_SupportDataDrivenLensFlare", false);
SetBool("m_SupportScreenSpaceLensFlare", false);
SetInt("m_GPUResidentDrawerMode", 0);
SetInt("m_VolumeFrameworkUpdateMode", 0);

urpSo.ApplyModifiedPropertiesWithoutUndo();
UnityEditor.EditorUtility.SetDirty(urp);
Say("URP-ассет настроен: " + rpFinal);

// ---------- 3. Рендерер ----------
var renderer = UnityEditor.AssetDatabase.LoadAssetAtPath<
    UnityEngine.Rendering.Universal.UniversalRendererData>(rendererFinal);
if (renderer == null)
{
    Say("FAIL: не найден рендерер " + rendererFinal);
}
else
{
    var rso = new UnityEditor.SerializedObject(renderer);
    var mode = rso.FindProperty("m_RenderingMode");
    if (mode != null) mode.intValue = 0;   // Forward: Forward+ требует compute-шейдеры
    else Say("Renderer: НЕТ СВОЙСТВА m_RenderingMode");

    var shadowTransparent = rso.FindProperty("m_ShadowTransparentReceive");
    if (shadowTransparent != null) shadowTransparent.boolValue = true;

    var nrp = rso.FindProperty("m_UseNativeRenderPass");
    if (nrp != null) nrp.boolValue = true;

    var priming = rso.FindProperty("m_DepthPrimingMode");
    if (priming != null) priming.intValue = 0;

    var itm = rso.FindProperty("m_IntermediateTextureMode");
    if (itm != null) itm.intValue = 0;     // Auto

    rso.ApplyModifiedPropertiesWithoutUndo();
    UnityEditor.EditorUtility.SetDirty(renderer);

    // SSAO-фича выключается: в сцене нет Volume, она всё равно не активна,
    // а включённая фича заставляет URP держать промежуточную текстуру.
    int disabled = 0;
    int seen = 0;
    var subs = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(rendererFinal);
    foreach (var sub in subs)
    {
        if (sub == null || sub == renderer) continue;
        if (sub.name.IndexOf("AmbientOcclusion", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
        seen++;
        var sso = new UnityEditor.SerializedObject(sub);
        var active = sso.FindProperty("m_Active");
        if (active != null && active.boolValue)
        {
            active.boolValue = false;
            sso.ApplyModifiedPropertiesWithoutUndo();
            UnityEditor.EditorUtility.SetDirty(sub);
            disabled++;
        }
    }
    Say("рендерер: Forward, SSAO найдено " + seen + " / выключено " + disabled);
}

// ---------- 4. QualitySettings: один уровень на все платформы ----------
var qs = UnityEditor.AssetDatabase.LoadMainAssetAtPath("ProjectSettings/QualitySettings.asset");
if (qs == null) { return log.ToString() + "FAIL: QualitySettings недоступен"; }

var qsSo = new UnityEditor.SerializedObject(qs);
var levels = qsSo.FindProperty("m_QualitySettings");
if (levels == null) { return log.ToString() + "FAIL: нет m_QualitySettings"; }

if (levels.arraySize > 1)
{
    // Оставляем PC-уровень (индекс 1) как единственный.
    levels.MoveArrayElement(levels.arraySize - 1, 0);
    levels.arraySize = 1;
}

var q = levels.GetArrayElementAtIndex(0);
// shadowDistance, shadowNearPlaneOffset, shadowCascade2Split, lodBias и
// resolutionScalingFixedDPIFactor — float, хотя читаются как «числа».
void QInt(string p, int v)
{
    var prop = q.FindPropertyRelative(p);
    if (prop == null) { Say("Quality: НЕТ СВОЙСТВА " + p); return; }
    if (prop.propertyType == UnityEditor.SerializedPropertyType.Float) prop.floatValue = v;
    else if (prop.propertyType == UnityEditor.SerializedPropertyType.Integer
          || prop.propertyType == UnityEditor.SerializedPropertyType.Enum) prop.intValue = v;
    else Say("Quality: НЕОЖИДАННЫЙ ТИП " + p + " (" + prop.propertyType + ")");
}
void QFloat(string p, float v)
{
    var prop = q.FindPropertyRelative(p);
    if (prop == null) { Say("Quality: НЕТ СВОЙСТВА " + p); return; }
    if (prop.propertyType == UnityEditor.SerializedPropertyType.Integer) prop.intValue = (int)v;
    else if (prop.propertyType == UnityEditor.SerializedPropertyType.Float) prop.floatValue = v;
    else Say("Quality: НЕОЖИДАННЫЙ ТИП " + p + " (" + prop.propertyType + ")");
}
void QBool(string p, bool v)
{
    var prop = q.FindPropertyRelative(p);
    if (prop == null) { Say("Quality: НЕТ СВОЙСТВА " + p); return; }
    if (prop.propertyType != UnityEditor.SerializedPropertyType.Boolean)
    {
        Say("Quality: НЕОЖИДАННЫЙ ТИП " + p + " (" + prop.propertyType + ")");
        return;
    }
    prop.boolValue = v;
}

q.FindPropertyRelative("name").stringValue = "Default";
QInt("pixelLightCount", 4);
QInt("shadows", 2);              // All
QInt("shadowResolution", 2);     // High
QInt("shadowProjection", 1);     // Stable Fit
QInt("shadowCascades", 4);
QFloat("shadowDistance", 50.0f);
QFloat("shadowNearPlaneOffset", 3.0f);
QFloat("shadowCascade2Split", 0.25f);
var c4 = q.FindPropertyRelative("shadowCascade4Split");
if (c4 != null) { c4.vector3Value = new UnityEngine.Vector3(0.1f, 0.25f, 0.5f); }
QInt("shadowmaskMode", 1);       // Distance Shadowmask
QInt("skinWeights", 4);
QInt("anisotropicTextures", 2);  // Forced on: снег под острым углом без анизотропии мылит
QInt("antiAliasing", 0);         // URP слушает камеру, не это поле
QBool("softParticles", false);
QBool("softVegetation", false);
QBool("realtimeReflectionProbes", false);
QInt("vSyncCount", 1);           // Every V Blank: в вебе всё равно капается rAF
QFloat("lodBias", 1.0f);
QInt("particleRaycastBudget", 64);
QBool("streamingMipmapsActive", false);
QInt("asyncUploadTimeSlice", 2);
QInt("asyncUploadBufferSize", 16);
QBool("asyncUploadPersistentBuffer", true);
QFloat("resolutionScalingFixedDPIFactor", 1.0f);

var rpRef = q.FindPropertyRelative("customRenderPipeline");
if (rpRef != null) rpRef.objectReferenceValue = urp;

var excluded = q.FindPropertyRelative("excludedTargetPlatforms");
if (excluded != null && excluded.arraySize != 0) excluded.arraySize = 0;

var current = qsSo.FindProperty("m_CurrentQuality");
if (current != null) current.intValue = 0;

// m_PerPlatformDefaultQuality не доступен через SerializedObject (нативное свойство
// без детей) и публичного сеттера у него нет. Единственный надёжный путь —
// QualitySettings.SetQualityLevel: он пишет индекс для АКТИВНОЙ платформы.
// Для WebGL это делает BuildWebGL.PerformBuild (в батче активная цель — WebGL).
UnityEngine.QualitySettings.SetQualityLevel(0, true);
Say("SetQualityLevel(0) для активной платформы "
    + UnityEditor.EditorUserBuildSettings.activeBuildTarget);

qsSo.ApplyModifiedPropertiesWithoutUndo();

// ---------- 5. GraphicsSettings: тот же ассет как дефолтный ----------
var gs = UnityEditor.AssetDatabase.LoadMainAssetAtPath("ProjectSettings/GraphicsSettings.asset");
if (gs != null)
{
    var gso = new UnityEditor.SerializedObject(gs);
    var crp = gso.FindProperty("m_CustomRenderPipeline");
    if (crp != null)
    {
        crp.objectReferenceValue = urp;
        gso.ApplyModifiedPropertiesWithoutUndo();
        Say("GraphicsSettings.m_CustomRenderPipeline = " + urp.name);
    }
}

// ---------- 6. Камера сцены: SMAA ----------
var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
string scenePath = scene.path;
if (scenePath != "Assets/Scenes/SnowLevel.unity")
{
    Say("камера НЕ тронута: активная сцена " + scenePath + " (ожидалась SnowLevel)");
}
else
{
    int cams = 0;
    foreach (var root in scene.GetRootGameObjects())
    {
        var extra = root.GetComponentsInChildren<
            UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(true);
        foreach (var c in extra)
        {
            var cso = new UnityEditor.SerializedObject(c);
            var aa = cso.FindProperty("m_Antialiasing");
            var aq = cso.FindProperty("m_AntialiasingQuality");
            if (aa != null) aa.intValue = 2;   // SubpixelMorphologicalAntiAliasing
            if (aq != null) aq.intValue = 2;   // High
            cso.ApplyModifiedPropertiesWithoutUndo();
            UnityEditor.EditorUtility.SetDirty(c);
            cams++;
        }
    }
    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
    UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
    Say("SMAA на камерах: " + cams);
}

// ---------- 7. Удаление второго (Mobile) набора ассетов ----------
// Требует запуска с --no-safety: execute_code блокирует удаление.
string[] junk = { "Assets/Settings/Mobile_RPAsset.asset", "Assets/Settings/Mobile_Renderer.asset" };
foreach (string path in junk)
{
    if (UnityEditor.AssetDatabase.LoadMainAssetAtPath(path) == null)
    {
        Say("удалено ранее: " + path);
        continue;
    }
    Say("delete " + path + ": " + (UnityEditor.AssetDatabase.DeleteAsset(path) ? "ok" : "ОТКАЗ"));
}

UnityEditor.AssetDatabase.SaveAssets();
UnityEditor.AssetDatabase.Refresh();

// Чистим Console: ошибки от предыдущих прогонов (неверный тип свойства) уже не
// относятся к текущему состоянию проекта.
var logEntries = System.Type.GetType("UnityEditor.LogEntries, UnityEditor.dll");
var clear = logEntries == null ? null
    : logEntries.GetMethod("Clear", System.Reflection.BindingFlags.Static
        | System.Reflection.BindingFlags.Public);
if (clear != null) { clear.Invoke(null, null); Say("Console очищена"); }
else Say("Console НЕ очищена: LogEntries.Clear не найден");

log.AppendLine("qualityLevels=" + UnityEngine.QualitySettings.names.Length
    + " [" + string.Join(",", UnityEngine.QualitySettings.names) + "]"
    + " index=" + UnityEngine.QualitySettings.GetQualityLevel());
log.AppendLine("activeRP=" + UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.name);

return log.ToString();
