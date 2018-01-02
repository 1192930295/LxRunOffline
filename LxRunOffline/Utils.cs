﻿using System;
using System.Security.Principal;

namespace LxRunOffline {
	static class Utils {
		static string promptAnswer = Environment.GetEnvironmentVariable("LXRUNOFFLINE_PROMPT_ANSWER");
		static bool showLog = Environment.GetEnvironmentVariable("LXRUNOFFLINE_VERBOSE") == "1";

		static void WriteLine(string output, ConsoleColor color) {
			Console.ForegroundColor = color;
			Console.Error.WriteLine(output);
			Console.ResetColor();
		}

		public static void Log(string output) {
			if (showLog) {
				Console.Error.WriteLine($"[LOG] {output}");
			}
		}

		public static void Warning(string output) {
			WriteLine($"[WARNING] {output}", ConsoleColor.Yellow);
		}

		public static void Error(string output) {
			WriteLine($"[ERROR] {output}", ConsoleColor.Red);
			Environment.Exit(1);
		}

		public static bool Prompt() {
			while (true) {
				Console.Write("Do you want to continue? (y/n): ");
				var keyChar = promptAnswer ?? Console.ReadKey().KeyChar.ToString();
				Console.WriteLine();
				if (keyChar == "y") return true;
				else if (keyChar == "n") return false;
				if (promptAnswer == null) Warning("Invalid input.");
				else Error($"Invalid environment variable: \"LXRUNOFFLINE_PROMPT_ANSWER={promptAnswer}\".");
			}
		}

		public static bool IsAdministrator()
		{
			var identity = WindowsIdentity.GetCurrent();
			var principal = new WindowsPrincipal(identity);
			return principal.IsInRole(WindowsBuiltInRole.Administrator);
		}
	}
}
