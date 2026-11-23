using System;
using System.Collections.Generic;

namespace Chat.Demo
{
    /// <summary>断言收集器：跑完一次性输出通过/失败统计，并给出进程退出码。</summary>
    internal sealed class Checker
    {
        private readonly List<string> _failures = new List<string>();

        /// <summary>已通过的数量。</summary>
        public int Passed { get; private set; }

        /// <summary>记录一条断言结果。</summary>
        public void Check(bool ok, string description)
        {
            if (ok)
            {
                Passed++;
                Console.WriteLine("  [通过] " + description);
            }
            else
            {
                _failures.Add(description);
                Console.WriteLine("  [失败] " + description);
            }
        }

        /// <summary>打印统计，返回进程退出码（0 = 全部通过）。</summary>
        public int Report()
        {
            Console.WriteLine();
            Console.WriteLine("=== 通过 " + Passed + " 项，失败 " + _failures.Count + " 项 ===");

            foreach (string failure in _failures)
            {
                Console.WriteLine("  失败: " + failure);
            }

            return _failures.Count == 0 ? 0 : 1;
        }
    }
}
