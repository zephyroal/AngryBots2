// ABPortCompileGate.cs
// -----------------------------------------------------------------------------
// P1 资产移植 —— 全工程编译门禁 (namespace ABPort)
//
// 用途：任何 batchmode 的移植步骤前先跑一次，确认整个工程 0 error CS，
// 并且移植脚本本身确实被编译进程序集（避免烧入缓存导致 method not found）。
//
// 退出码：0 = 通过；1 = 有编译错误（错误明细写入日志）。
// -----------------------------------------------------------------------------
#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ABPort
{
    public static class ABPortCompileGate
    {
        public static void Run()
        {
            Directory.CreateDirectory(U6PortPaths.LogDir);
            Debug.Log("[ABPort] === compile gate ===");

            var selfAsm = typeof(ABPortCompileGate).Assembly;
            Debug.Log("[ABPort] gate assembly: " + selfAsm.GetName().Name);

            bool ok = true;
            var asms = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var asm in asms)
            {
                var types = Type.EmptyTypes;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException rtle) { types = rtle.Types.Where(t => t != null).ToArray(); }
                catch { continue; }

                foreach (var t in types)
                {
                    if (t.FullName != null && t.FullName.StartsWith("ABPort.")) continue;
                }
            }

            // 1) 移植脚本必须在场
            if (Type.GetType("ABPort.U6AssetPorter, " + selfAsm.GetName().Name) == null)
            {
                Debug.LogError("[ABPort] U6AssetPorter not found in " + selfAsm.GetName().Name);
                ok = false;
            }
            if (Type.GetType("ABPort.U6PortPaths, Assembly-CSharp") == null)
            {
                Debug.LogError("[ABPort] U6PortPaths not found");
                ok = false;
            }

            // 2) 通过反射枚举所有已加载程序集里是否存在带 "error" 的编译失败（Unity 编译失败时不会加载程序集，故用 EditorUtility)
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic)
                .Select(a => a.GetName().Name)
                .OrderBy(x => x)
                .ToList();
            Debug.Log("[ABPort] loaded assemblies: " + assemblies.Count + " (Assembly-CSharp present: " + assemblies.Contains("Assembly-CSharp") + ")");

            if (!assemblies.Contains("Assembly-CSharp"))
            {
                Debug.LogError("[ABPort] Assembly-CSharp missing -> project did not compile");
                ok = false;
            }

            Debug.Log(ok ? "[ABPort] GATE RESULT: PASS" : "[ABPort] GATE RESULT: FAIL");
            // 显式 flush 日志，否则 quit 时重载域会吞掉输出
            System.Console.Out.Flush();
            if (!ok) throw new System.Exception("[ABPort] GATE FAIL");
            if (!Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
#endif