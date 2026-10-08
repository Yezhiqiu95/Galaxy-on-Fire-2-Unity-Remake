// ChineseLocalizationSetup.cs  (Editor only)
//
// 简体中文本地化的一键配置：创建中文字体资产并接入字体回退链。
//
// 为什么需要这一步：
//   Assets/UI/GoF2Common.uss 把 UI Toolkit 的默认字体锁定为 Serpentine-ICG-Light-SDF。
//   三套现有 SDF 字体（Serpentine / Inter-Regular / Inter-SemiBold）都是 Dynamic + 多图集模式，
//   字形在运行时按需光栅化 —— 但它们的字形源里一个汉字都没有，动态模式也变不出来，
//   于是中文只能渲染成方块 □。解决办法是在回退链尾部接一个含中文字形的字体资产。
//
// 本脚本做四件事（全部幂等，可反复运行）：
//   1. 由 Assets/UI/Fonts/NotoSansSC-Regular.otf 生成 NotoSansSC-Regular-SDF.asset
//      （TMP 默认参数：采样 90 / 内边距 9 / SDFAA / 1024×1024 / Dynamic / 允许多图集，
//        与现有三套字体完全一致，因此回退时字号与基线不会跳变）
//   2. 把该资产追加到上述三套字体的 Fallback Font Assets 列表尾部
//   3. 建立 Assets/UI/GoF2PanelTextSettings.asset 并挂到 GoF2PanelSettings.textSettings
//      （UI Toolkit 的全局回退链，双保险）
//   4. 校验 Resources/GoF2LanguageTables.asset 里已登记 zh-Hans，未登记则补上
//
// 用法：
//   菜单  GoF2/中文本地化/一键配置中文字体
//   或   Unity -batchmode -quit -projectPath <项目> -executeMethod GoF2Remake.EditorTools.ChineseLocalizationSetup.RunBatch

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
// Unity 7000 里 TextAsset 在 UnityEngine 与 UnityEngine.TextCore.Text 中各有一个定义，
// 必须显式指定为经典版（LanguageTables.tables 用的就是它），否则 CS0104
using TextAsset = UnityEngine.TextAsset;

namespace GoF2Remake.EditorTools
{
    [InitializeOnLoad]
    public static class ChineseLocalizationSetup
    {
        const string FontsDir = "Assets/UI/Fonts";
        const string SourceFontPath = FontsDir + "/NotoSansSC-Regular.otf";
        const string CjkFontAssetPath = FontsDir + "/NotoSansSC-Regular-SDF.asset";
        const string PanelSettingsPath = "Assets/UI/GoF2PanelSettings.asset";
        const string PanelTextSettingsPath = "Assets/UI/GoF2PanelTextSettings.asset";
        const string SerpentinePath = FontsDir + "/Serpentine-ICG-Light-SDF.asset";

        const string LocaleCode = "zh-Hans";
        const string TextTablePath = "Assets/Localization/text_zh-Hans.json";
        const string LanguageTablesPath = "Assets/Resources/GoF2LanguageTables.asset";

        // remake 自己新增的界面文字（代码里的 Localization.Extra(key, 英文) 兜底）走 Localization.LoadExtra：
        // Resources/GoF2Localization/extra_<语言>.json，{"key": "译文"}，缺键就回落到英文。
        const string ExtraTableDir = "Assets/Resources/GoF2Localization";

        static readonly string[] HostFontAssets =
        {
            SerpentinePath,
            FontsDir + "/Inter-Regular-SDF.asset",
            FontsDir + "/Inter-SemiBold-SDF.asset",
        };

        static bool autoRunScheduled;

        static ChineseLocalizationSetup()
        {
            // 首次打开项目时自动配置一次（资产创建必须等导入结束，故用 delayCall 并检查更新状态）。
            EditorApplication.delayCall += AutoRun;
        }

        static void AutoRun()
        {
            if (autoRunScheduled) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += AutoRun;   // 再等一轮
                return;
            }
            autoRunScheduled = true;

            // 已配置过就直接跳过，不在每次域重载时反复写资产。
            if (AssetDatabase.LoadAssetAtPath<FontAsset>(CjkFontAssetPath) != null &&
                AssetDatabase.LoadAssetAtPath<PanelTextSettings>(PanelTextSettingsPath) != null)
                return;

            try
            {
                var log = Configure(verbose: false);
                if (!string.IsNullOrEmpty(log))
                    Debug.Log("GoF2 中文本地化：已自动完成配置。\n" + log);
            }
            catch (Exception e)
            {
                Debug.LogWarning("GoF2 中文本地化：自动配置未完成，请手动运行菜单 GoF2/中文本地化/一键配置中文字体\n" + e);
            }
        }

        /// <summary>供其他编辑器脚本（例如打包流程）调用：执行配置并返回改动日志，失败时抛异常。</summary>
        public static string ConfigureNow() => Configure(verbose: true);

        [MenuItem("GoF2/中文本地化/一键配置中文字体", priority = 60)]
        public static void ConfigureMenu()
        {
            var log = Configure(verbose: true);
            EditorUtility.DisplayDialog("GoF2 中文本地化",
                string.IsNullOrEmpty(log) ? "配置完成（原本已是正确状态）。" : "配置完成：\n\n" + log, "好");
        }

        [MenuItem("GoF2/中文本地化/检查简体中文配置", priority = 61)]
        public static void VerifyMenu()
        {
            var issues = new List<string>();

            if (AssetDatabase.LoadAssetAtPath<TextAsset>(TextTablePath) == null)
                issues.Add("缺少文本表 " + TextTablePath);

            var extraPath = ExtraTableDir + "/extra_" + LocaleCode + ".json";
            if (AssetDatabase.LoadAssetAtPath<TextAsset>(extraPath) == null)
                issues.Add("缺少扩展词典 " + extraPath + "（remake 自有文字会回落成英文）");

            var cjk = AssetDatabase.LoadAssetAtPath<FontAsset>(CjkFontAssetPath);
            if (cjk == null)
                issues.Add("缺少中文字体资产 " + CjkFontAssetPath + "（运行“一键配置中文字体”）");
            else
            {
                var probe = cjk.HasCharacter('中') && cjk.HasCharacter('简');
                if (!probe) issues.Add("中文字体资产未覆盖汉字“中/简”");

                foreach (var path in HostFontAssets)
                {
                    var host = AssetDatabase.LoadAssetAtPath<FontAsset>(path);
                    if (host == null) { issues.Add("找不到宿主字体 " + path); continue; }
                    bool linked = host.fallbackFontAssetTable != null &&
                                  host.fallbackFontAssetTable.Any(f => f == cjk || (f != null && f.name == cjk.name));
                    if (!linked) issues.Add("字体回退链未包含中文：" + path);
                }

                if (cjk.sourceFontFile == null)
                    issues.Add("中文字体资产没有源字体引用 —— 打包后会缺字形（请重新运行一键配置）");
            }

            var lt = AssetDatabase.LoadAssetAtPath<LanguageTables>(LanguageTablesPath);
            if (lt == null || lt.codes == null || Array.IndexOf(lt.codes, LocaleCode) < 0)
                issues.Add("Resources/GoF2LanguageTables.asset 未登记 " + LocaleCode);

            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/MainMenu.unity");
            if (scene == null) issues.Add("找不到 Assets/Scenes/MainMenu.unity");

            if (issues.Count == 0)
            {
                Debug.Log("GoF2 中文本地化：检查通过，简体中文配置完整。");
                EditorUtility.DisplayDialog("GoF2 中文本地化", "检查通过：简体中文配置完整。", "好");
            }
            else
            {
                foreach (var s in issues) Debug.LogWarning("GoF2 中文本地化：" + s);
                EditorUtility.DisplayDialog("GoF2 中文本地化",
                    "发现 " + issues.Count + " 个问题（详见 Console）：\n\n" + string.Join("\n", issues.ToArray()), "好");
            }
        }

        /// <summary>batchmode 入口（-executeMethod 调用）。出错时以非 0 退出码结束。</summary>
        public static void RunBatch()
        {
            try
            {
                var log = Configure(verbose: true);
                Debug.Log("GoF2 中文本地化：batchmode 配置完成。\n" + log);
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("GoF2 中文本地化：batchmode 配置失败。\n" + e);
                EditorApplication.Exit(1);
            }
        }

        // ---- 实现 ---------------------------------------------------------------------------------

        /// <summary>返回做了哪些改动的说明；已是最新状态时返回空字符串。</summary>
        static string Configure(bool verbose)
        {
            var log = new List<string>();

            var src = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
            if (src == null)
                throw new Exception("找不到中文字体源文件 " + SourceFontPath +
                                    "（应为一个含中文字形的 .otf/.ttf，随项目一起提交）");

            // 若文件被 Unity 当成 OTF 导入失败（例如被误当作二进制），这里会拿到 null。
            if (verbose) log.Add("源字体: " + src.name + "  (family " + src.name + ")");

            // 1) 中文字体资产
            var cjk = AssetDatabase.LoadAssetAtPath<FontAsset>(CjkFontAssetPath);
            if (cjk == null)
            {
                // CreateFontAsset 的默认参数与现有三套 SDF 字体一致：
                // pointSize 90 / padding 9 / SDFAA / 1024x1024 / Dynamic / 多图集开
                cjk = FontAsset.CreateFontAsset(src);
                cjk.name = "NotoSansSC-Regular-SDF";
                AssetDatabase.CreateAsset(cjk, CjkFontAssetPath);

                if (cjk.material != null)
                {
                    cjk.material.name = cjk.name + " Material";
                    AssetDatabase.AddObjectToAsset(cjk.material, cjk);
                }
                if (cjk.atlasTextures != null)
                {
                    foreach (var tex in cjk.atlasTextures)
                    {
                        if (tex == null) continue;
                        tex.name = cjk.name + " Atlas";
                        AssetDatabase.AddObjectToAsset(tex, cjk);
                    }
                }
                EditorUtility.SetDirty(cjk);
                log.Add("新建字体资产: " + CjkFontAssetPath);
            }
            else if (verbose)
            {
                log.Add("字体资产已存在: " + CjkFontAssetPath);
            }

            // 动态字体必须能在运行时读到源字体，否则字形取不到（打包后表现为方块或空白）。
            if (cjk.sourceFontFile == null)
                throw new Exception("中文字体资产缺少源字体引用，无法在运行时光栅化字形。" +
                                    "请删除 " + CjkFontAssetPath + " 后重新运行。");

            // 2) 回退链
            foreach (var path in HostFontAssets)
            {
                var host = AssetDatabase.LoadAssetAtPath<FontAsset>(path);
                if (host == null) continue;
                if (host.fallbackFontAssetTable == null)
                    host.fallbackFontAssetTable = new List<FontAsset>();

                bool present = host.fallbackFontAssetTable.Any(f => f == cjk);
                if (!present)
                {
                    host.fallbackFontAssetTable.Add(cjk);
                    EditorUtility.SetDirty(host);
                    log.Add("回退链已加入中文: " + path);
                }
            }
            AssetDatabase.SaveAssets();

            // 3) UI Toolkit 全局文本设置（第二道保险）
            var pts = AssetDatabase.LoadAssetAtPath<PanelTextSettings>(PanelTextSettingsPath);
            if (pts == null)
            {
                pts = ScriptableObject.CreateInstance<PanelTextSettings>();
                AssetDatabase.CreateAsset(pts, PanelTextSettingsPath);
                log.Add("新建 PanelTextSettings: " + PanelTextSettingsPath);
            }

            var ptsSo = new SerializedObject(pts);
            var serpentine = AssetDatabase.LoadAssetAtPath<FontAsset>(SerpentinePath);
            var it = ptsSo.GetIterator();
            while (it.NextVisible(true))
            {
                if (it.propertyType == SerializedPropertyType.ObjectReference &&
                    it.name.Equals("m_DefaultFontAsset", StringComparison.OrdinalIgnoreCase) &&
                    it.objectReferenceValue == null && serpentine != null)
                {
                    it.objectReferenceValue = serpentine;
                }
                // 注：Unity 7000 的 FontAsset 没有 spriteAsset 属性，
                // m_DefaultSpriteAsset 留空即可，不影响文字渲染
                else if (it.isArray &&
                         it.name.IndexOf("FallbackFontAssets", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    int n = it.arraySize;
                    bool found = false;
                    for (int i = 0; i < n; i++)
                    {
                        var el = it.GetArrayElementAtIndex(i);
                        if (el.objectReferenceValue == cjk) { found = true; break; }
                    }
                    if (!found)
                    {
                        it.arraySize = n + 1;
                        it.GetArrayElementAtIndex(n).objectReferenceValue = cjk;
                        log.Add("PanelTextSettings 回退列表已加入中文");
                    }
                }
            }
            ptsSo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pts);

            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (ps != null)
            {
                var psSo = new SerializedObject(ps);
                var prop = psSo.FindProperty("textSettings");
                if (prop != null && prop.objectReferenceValue == null)
                {
                    prop.objectReferenceValue = pts;
                    psSo.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(ps);
                    log.Add("GoF2PanelSettings.textSettings 已指向新的 PanelTextSettings");
                }
            }

            AssetDatabase.SaveAssets();

            // 4) 语言表兜底登记（正常情况下场景构建脚本已经写好，这里只防漏）
            var table = AssetDatabase.LoadAssetAtPath<TextAsset>(TextTablePath);
            if (table == null)
                throw new Exception("找不到文本表 " + TextTablePath);

            var lt = AssetDatabase.LoadAssetAtPath<LanguageTables>(LanguageTablesPath);
            if (lt != null && (lt.codes == null || Array.IndexOf(lt.codes, LocaleCode) < 0))
            {
                lt.codes = (lt.codes ?? new string[0]).Concat(new[] { LocaleCode }).ToArray();
                lt.tables = (lt.tables ?? new TextAsset[0]).Concat(new[] { table }).ToArray();
                EditorUtility.SetDirty(lt);
                AssetDatabase.SaveAssets();
                log.Add("GoF2LanguageTables.asset 已登记 " + LocaleCode);
            }

            AssetDatabase.Refresh();
            return log.Count == 0 ? "" : string.Join("\n", log.ToArray());
        }
    }
}
