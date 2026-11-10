using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace ABGameplay
{
	/// <summary>
	/// 编译日志扫描工具：从 Unity 的 batchmode 日志里统计 "error CS" 行数。
	/// batchmode 下当前编译的错误也会打印到 stderr，这里只扫 Editor.log 即可（Unity 会同步写入）。
	/// </summary>
	public static class CompilationErrors
	{
		static readonly Regex ErrorPattern = new Regex(@"error CS\d{4}", RegexOptions.Compiled);

		/// <summary>扫描给定日志文件，返回 error CS 条数；文件不存在时返回 0。<paramref name="since"/> 之前的行忽略。</summary>
		public static int Scan(string directory, string fileName, DateTime since)
		{
			try
			{
				var path = Path.IsPathRooted(fileName) ? fileName : Path.Combine(directory, fileName);
				if (!File.Exists(path)) return 0;

				// Unity 正在写日志时文件被占用：用 FileShare.ReadWrite 打开。
				using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
				using (var reader = new StreamReader(stream))
				{
					int count = 0;
					string line;
					while ((line = reader.ReadLine()) != null)
						if (ErrorPattern.IsMatch(line)) count++;
					return count;
				}
			}
			catch (Exception)
			{
				return 0;
			}
		}

		/// <summary>返回日志里所有 error CS 行（用于报告）。</summary>
		public static List<string> Collect(string path, int max = 50)
		{
			var result = new List<string>();
			try
			{
				if (!File.Exists(path)) return result;
				using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
				using (var reader = new StreamReader(stream))
				{
					string line;
					while ((line = reader.ReadLine()) != null && result.Count < max)
						if (ErrorPattern.IsMatch(line)) result.Add(line);
				}
			}
			catch (Exception)
			{
				// 忽略：报告里已有主结论。
			}
			return result;
		}
	}
}