using MelonLoader;
using System;
using System.Linq;
using System.Reflection;
using System.Globalization;

namespace Logic
{
    public static class StringCaller
    {
        /// <summary>
        /// 按 "Namespace.Class.Method" 调用一个静态方法
        /// </summary>
        public static object CallStatic(string path, params object[] args)
        {
            int lastDot = path.LastIndexOf('.');
            if (lastDot <= 0) return null;

            string typeName = path.Substring(0, lastDot);
            string methodName = path.Substring(lastDot + 1);

            Type type = FindType(typeName);
            if (type == null) return null;

            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

            // 1. 先按原类型精确匹配
            Type[] argTypes = args?.Select(a => a?.GetType() ?? typeof(object)).ToArray()
                              ?? Type.EmptyTypes;

            var exact = type.GetMethod(methodName, flags, null, argTypes, null);

            if (exact != null)
            {
                try { return exact.Invoke(null, args); }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[Call] {path} 调用异常: {ex.InnerException?.Message ?? ex.Message}");
                    return null;
                }
            }

            // 2. 找不到 → 遍历所有同名方法，用 TypeConverterEx 尝试转换
            foreach (var m in type.GetMethods(flags))
            {
                if (m.Name != methodName) continue;

                var ps = m.GetParameters();
                if (ps.Length != (args?.Length ?? 0)) continue;

                var converted = new object[args.Length];
                bool ok = true;

                for (int i = 0; i < args.Length; i++)
                {
                    if (!TypeConverterEx.TryConvert(args[i], ps[i].ParameterType, out converted[i]))
                    {
                        ok = false;
                        break;
                    }
                }

                if (!ok) continue;

                try
                {
                    return m.Invoke(null, converted);
                }
                catch (Exception ex)
                {
                    MelonLogger.Error(
                        $"[Call] {path} 调用异常（尝试 {m}）: {ex.InnerException?.Message ?? ex.Message}");
                    // 继续试下一个重载
                }
            }

            MelonLogger.Warning($"[Call] 找不到合适的方法或参数转换失败: {path}");
            return null;
        }
        private static Type FindType(string fullName)
        {
            var t = Type.GetType(fullName);
            if (t != null) return t;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    t = asm.GetType(fullName);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }

        public static void update(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            // ★ 去掉空项，多个空格不出空字符串
            var msg = message.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (msg.Length == 0) return;

            // ★ 忽略大小写
            if (!msg[0].Equals("call", StringComparison.OrdinalIgnoreCase)) return;

            // ★ 必须至少有一个参数（路径）
            if (msg.Length < 2)
            {
                MelonLogger.Warning("[Call] 语法: call <路径> [参数...]");
                return;
            }

            string path = msg[1];
            var rest = msg.Skip(2).ToArray();

            // string[] 协变传给 params object[]，OK
            CallStatic(path, rest);
        }
    }


public static class TypeConverterEx
    {
        /// <summary>
        /// 尝试把 value 转成 targetType。失败返回 null（用 TryConvert 判断）。
        /// </summary>
        public static bool TryConvert(object value, Type targetType, out object result)
        {
            result = null;

            if (targetType == null) return false;

            // ---------- 1. null 处理 ----------
            if (value == null)
            {
                // 引用类型 / Nullable 可以接收 null
                if (!targetType.IsValueType) return true;   // result 保持 null
                if (Nullable.GetUnderlyingType(targetType) != null) return true;
                return false;   // 值类型不接受 null
            }

            // ---------- 2. 已经是目标类型 ----------
            if (targetType.IsInstanceOfType(value))
            {
                result = value;
                return true;
            }

            // ---------- 3. Nullable<T> 拆开 ----------
            var underlying = Nullable.GetUnderlyingType(targetType);
            if (underlying != null)
            {
                if (TryConvert(value, underlying, out var inner))
                {
                    result = inner;
                    return true;
                }
                return false;
            }

            // ---------- 4. string 目标 ----------
            if (targetType == typeof(string))
            {
                result = value.ToString();
                return true;
            }

            // ---------- 5. bool ----------
            if (targetType == typeof(bool))
            {
                if (value is bool b) { result = b; return true; }
                string s = value.ToString().Trim().ToLowerInvariant();
                if (s == "1" || s == "true" || s == "yes" || s == "on") { result = true; return true; }
                if (s == "0" || s == "false" || s == "no" || s == "off") { result = false; return true; }
                return false;
            }

            // ---------- 6. char ----------
            if (targetType == typeof(char))
            {
                string s = value.ToString();
                if (s.Length == 1) { result = s[0]; return true; }
                return false;
            }

            // ---------- 7. enum ----------
            if (targetType.IsEnum)
            {
                if (value is string es)
                {
                    try
                    {
                        result = Enum.Parse(targetType, es, ignoreCase: true);
                        return true;
                    }
                    catch { return false; }
                }
                try
                {
                    // 数字转 enum
                    var underlyingType = Enum.GetUnderlyingType(targetType);
                    var num = Convert.ChangeType(value, underlyingType, CultureInfo.InvariantCulture);
                    result = Enum.ToObject(targetType, num);
                    return true;
                }
                catch { return false; }
            }

            // ---------- 8. 数值类型（float/double/int/...） ----------
            if (IsNumeric(targetType))
            {
                string s = value.ToString().Trim();

                // 空字符串视为失败
                if (s.Length == 0) return false;

                try
                {
                    // ★ 用 InvariantCulture，避免 "1.5" 在部分地区变成 "15"
                    result = Convert.ChangeType(s, targetType, CultureInfo.InvariantCulture);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            // ---------- 9. Il2Cpp 类型映射 ----------
            // Il2Cpp 基础类型包装，需要特别处理
            string typeName = targetType.FullName ?? "";
            if (typeName.StartsWith("Il2CppSystem."))
            {
                // Il2CppSystem.Single / Double / Int32 / Boolean / String ...
                string shortName = typeName.Substring("Il2CppSystem.".Length);

                // 先按底层 C# 类型转换
                Type nativeType = shortName switch
                {
                    "Single" => typeof(float),
                    "Double" => typeof(double),
                    "Int32" => typeof(int),
                    "Int64" => typeof(long),
                    "Int16" => typeof(short),
                    "UInt32" => typeof(uint),
                    "UInt64" => typeof(ulong),
                    "UInt16" => typeof(ushort),
                    "Byte" => typeof(byte),
                    "SByte" => typeof(sbyte),
                    "Boolean" => typeof(bool),
                    "Char" => typeof(char),
                    _ => null,
                };

                if (nativeType != null && TryConvert(value, nativeType, out var nativeVal))
                {
                    try
                    {
                        // 转成 Il2Cpp 包装类型（用 Activator 走隐式转换也行，但简单起见用反射）
                        // 大部分 Il2Cpp 包装类型有 op_Implicit，可以直接赋值
                        result = Activator.CreateInstance(targetType, nativeVal);
                        return true;
                    }
                    catch
                    {
                        // 尝试直接赋值（部分 Il2Cpp 包装类型是隐式转换）
                        result = nativeVal;
                        return true;
                    }
                }
            }

            // ---------- 10. 兜底：Convert.ChangeType ----------
            try
            {
                result = Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsNumeric(Type t)
        {
            return t == typeof(byte) || t == typeof(sbyte)
                || t == typeof(short) || t == typeof(ushort)
                || t == typeof(int) || t == typeof(uint)
                || t == typeof(long) || t == typeof(ulong)
                || t == typeof(float) || t == typeof(double)
                || t == typeof(decimal);
        }
    }
}
