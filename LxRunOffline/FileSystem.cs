﻿using System;
using System.IO;
using Microsoft.Win32.SafeHandles;
using ICSharpCode.SharpZipLib.Tar;
using System.Text;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Collections.Generic;

namespace LxRunOffline {
	static class FileSystem {
		[StructLayout(LayoutKind.Sequential)]
		class LxssEaData {
			public short Reserved1;
			public short Version = 1;
			public int Mode;
			public int Uid;
			public int Gid;
			public int Reserved2;
			public int AtimeNsec;
			public int MtimeNsec;
			public int CtimeNsec;
			public long Atime;
			public long Mtime;
			public long Ctime;
		}

		[DllImport("LxssFileSystem.dll", CallingConvention = CallingConvention.Cdecl)]
		static extern SafeFileHandle GetFileHandle(
			[MarshalAs(UnmanagedType.LPWStr)]string ntPath,
			bool directory,
			bool create,
			bool write
		);

		[DllImport("LxssFileSystem.dll", CallingConvention = CallingConvention.Cdecl)]
		static extern bool EnumerateDirectory(
			SafeFileHandle hFile,
			[MarshalAs(UnmanagedType.LPWStr)]out string fileName,
			out bool directory
		);

		[DllImport("LxssFileSystem.dll", CallingConvention = CallingConvention.Cdecl)]
		static extern bool MakeHardLink(
			SafeFileHandle hTarget,
			[MarshalAs(UnmanagedType.LPWStr)]string linkName
		);

		[DllImport("LxssFileSystem.dll", CallingConvention = CallingConvention.Cdecl)]
		static extern bool GetFileId(SafeFileHandle hFile, out long id);

		[DllImport("LxssFileSystem.dll", CallingConvention = CallingConvention.Cdecl)]
		static extern int GetHardLinkCount(SafeFileHandle hFile);

		[DllImport("LxssFileSystem.dll", CallingConvention = CallingConvention.Cdecl)]
		static extern bool CopyLxssEa(SafeFileHandle hFrom, SafeFileHandle hTo);

		[DllImport("LxssFileSystem.dll", CallingConvention = CallingConvention.Cdecl)]
		static extern bool SetLxssEa(SafeFileHandle hFile, LxssEaData data, int dataLength);

		static string ToNtPath(this string path) => $@"\??\{path}";

		static string ToWslPath(this string path) {
			var sb = new StringBuilder();
			foreach (var c in path.TrimStart('/')) {
				if (c == '/') sb.Append('\\');
				else if (c >= 1 && c <= 31 || c == '<' || c == '>' || c == ':' || c == '"' || c == '\\' || c == '|' || c == '*' || c == '#') {
					sb.Append('#');
					sb.Append(((int)c).ToString("X4"));
				} else {
					sb.Append(c);
				}
			}
			return sb.ToString();
		}

		static string ToExactPath(this string path) {
			var dir = new DirectoryInfo(path);
			if (dir.Parent != null) {
				return Path.Combine(dir.Parent.FullName.ToExactPath(), dir.Parent.GetFileSystemInfos(dir.Name)[0].Name);
			} else {
				return dir.Name.ToUpper();
			}
		}

		static string StripRootPath(this string path, string rootPath) {
			if (rootPath == null) return path;
			if (!rootPath.EndsWith("/")) rootPath += '/';
			return path != rootPath && path.StartsWith(rootPath) ? path.Substring(rootPath.Length) : null;
		}

		static void CheckFileHandle(SafeFileHandle hFile, string path) {
			if (hFile.IsInvalid) {
				Utils.Error($"Couldn't open the file or directory \"{path}\".");
			}
		}

		static void CheckFileSystem(string path) {
			if (new DriveInfo(path[0].ToString()).DriveFormat != "NTFS") {
				Utils.Error("Only NTFS filesystems are suppoprted.");
			}
		}

		

		public static void CopyDirectory(string oldPath, string newPath) {
			oldPath = oldPath.ToExactPath();
			newPath = newPath.ToExactPath();
			Utils.Log($"Copying the directory \"{oldPath}\" to \"{newPath}\".");
			CheckFileSystem(oldPath);
			CheckFileSystem(newPath);
			var idDict = new Dictionary<long, string>();

			void copyDirectory(string oldDirPath, string newDirPath) {
				using (var hDir = GetFileHandle(oldDirPath.ToNtPath(), true, false, false)) {
					CheckFileHandle(hDir, oldDirPath);

					while (true) {
						if (!EnumerateDirectory(hDir, out var fileName, out var isDir)) {
							Utils.Error($"Couldn't get the contents of the directory \"{oldDirPath}\".");
						}
						if (fileName == null) break;
						if (fileName == "." || fileName == "..") continue;

						var oldFilePath = Path.Combine(oldDirPath, fileName);
						var newFilePath = Path.Combine(newDirPath, fileName);

						using (var hOld = GetFileHandle(oldFilePath.ToNtPath(), isDir, false, false)) {
							CheckFileHandle(hOld, oldFilePath);

							var count = GetHardLinkCount(hOld);
							if (count == 0) Utils.Error($"Couldn't get count of hard links of the file \"{oldFilePath}\".");
							if (!GetFileId(hOld, out var id)) {
								Utils.Error($"Couldn't get the index number of the file \"{oldFilePath}\".");
							}

							if (count == 1 || !idDict.ContainsKey(id)) {
								using (var hNew = GetFileHandle(newFilePath.ToNtPath(), isDir, true, true)) {
									CheckFileHandle(hNew, newFilePath);

									if (!CopyLxssEa(hOld, hNew)) {
										Utils.Error($"Couldn't copy extended attributes from \"{oldFilePath}\" to \"{newFilePath}\".");
									}

									if (!isDir) {
										using (var fsOld = new FileStream(hOld, FileAccess.Read))
										using (var fsNew = new FileStream(hNew, FileAccess.Write)) {
											fsOld.CopyTo(fsNew);
										}
									}
								}

								if (count > 1) idDict.Add(id, newFilePath);
							} else {
								var linkTargetPath = idDict[id];
								using (var hTarget = GetFileHandle(linkTargetPath.ToNtPath(), false, false, false)) {
									CheckFileHandle(hTarget, linkTargetPath);
									if (!MakeHardLink(hTarget, newFilePath.ToNtPath())) {
										Utils.Error($"Couldn't create the hard link from \"{newFilePath}\" to \"{linkTargetPath}\".");
									}
								}
							}
						}

						if (isDir) copyDirectory(oldFilePath, newFilePath);
					}
				}
			}

			copyDirectory(oldPath, newPath);
		}

		public static void DeleteDirectory(string path) {
			Utils.Log($"Deleting the directory \"{path}\".");
			var retryCount = 3;
			while (true) {
				retryCount--;
				try {
					Directory.Delete(path, true);
					return;
				} catch (Exception e) {
					Utils.Warning($"Couldn't delete the directory \"{path}\": {e.Message}");
					if (retryCount == 0) {
						Utils.Error($"You may have to delete it manually.");
					} else {
						Utils.Warning($"Retrying.");
						Thread.Sleep(500);
					}
				}
			}
		}

		public static void ExtractTar(Stream stream, string tarRootPath, string targetPath) {
			targetPath = targetPath.ToExactPath();
			Utils.Log($"Extracting the tar file to \"{targetPath}\".");
			CheckFileSystem(targetPath);

			using (var tar = new TarInputStream(stream)) {
				while (true) {
					var entry = tar.GetNextEntry();
					if (entry == null) break;

					var type = entry.TarHeader.TypeFlag;
					if (type == TarHeader.LF_DIR && Regex.IsMatch(entry.Name, @"(^|/)\.\.?/?$")) continue;
					var newFilePath = entry.Name.StripRootPath(tarRootPath);
					if (newFilePath == null || newFilePath == "etc/resolv.conf") continue;
					newFilePath = Path.Combine(targetPath, newFilePath.ToWslPath());

					if (type == TarHeader.LF_LINK) {
						var linkTargetPath = entry.TarHeader.LinkName.StripRootPath(tarRootPath);
						if (linkTargetPath == null) {
							Utils.Warning($"Igoring the hard link \"{newFilePath}\" to \"{entry.TarHeader.LinkName}\", which points to a location out of the specified root directory.");
							continue;
						}
						linkTargetPath = Path.Combine(targetPath, linkTargetPath.ToWslPath());

						using (var hTarget = GetFileHandle(linkTargetPath.ToNtPath(), false, false, false)) {
							CheckFileHandle(hTarget, linkTargetPath);
							if (!MakeHardLink(hTarget, newFilePath.ToNtPath())) {
								Utils.Error($"Couldn't create the hard link from \"{newFilePath}\" to \"{linkTargetPath}\".");
							}
						}
					} else {
						using (var hNew = GetFileHandle(newFilePath.ToNtPath(), type == TarHeader.LF_DIR, true, true)) {
							CheckFileHandle(hNew, newFilePath);

							var eaData = new LxssEaData {
								Mode = entry.TarHeader.Mode,
								Uid = entry.UserId,
								Gid = entry.GroupId
							};
							DateTimeOffset modTime = DateTime.SpecifyKind(entry.ModTime, DateTimeKind.Utc);
							eaData.Atime = eaData.Mtime = eaData.Ctime = modTime.ToUnixTimeSeconds();

							switch (type) {
							case TarHeader.LF_DIR:
								eaData.Mode |= 0x4000;
								break;
							case TarHeader.LF_SYMLINK:
								eaData.Mode |= 0xa000;
								break;
							case TarHeader.LF_OLDNORM:
								goto case TarHeader.LF_NORMAL;
							case TarHeader.LF_NORMAL:
								eaData.Mode |= 0x8000;
								break;
							default:
								Utils.Warning($"Ignoring the file \"{entry.Name}\" of unsupported file type '{(char)type}'.");
								continue;
							}

							if (!SetLxssEa(hNew, eaData, Marshal.SizeOf(typeof(LxssEaData)))) {
								Utils.Error($"Couldn't set extended attributes of \"{newFilePath}\".");
							}

							if (type == TarHeader.LF_DIR) continue;
							using (var fsNew = new FileStream(hNew, FileAccess.Write)) {
								if (entry.TarHeader.TypeFlag == TarHeader.LF_SYMLINK) {
									var linkData = Encoding.UTF8.GetBytes(entry.TarHeader.LinkName);
									fsNew.Write(linkData, 0, linkData.Length);
								} else {
									tar.CopyEntryContents(fsNew);
								}
							}
						}
					}
				}
			}

			var resolvPath = Path.Combine(targetPath, @"etc\resolv.conf");
			using (var hFile = GetFileHandle(resolvPath.ToNtPath(), false, true, true)) {
				CheckFileHandle(hFile, resolvPath);

				var eaData = new LxssEaData { Mode = 0b1010000111111111 };
				eaData.Atime = eaData.Mtime = eaData.Ctime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
				if (!SetLxssEa(hFile, eaData, Marshal.SizeOf(typeof(LxssEaData)))) {
					Utils.Error($"Couldn't set extended attributes of \"{resolvPath}\".");
				}

				using (var writer = new StreamWriter(new FileStream(hFile, FileAccess.Write))) {
					writer.Write("../run/resolvconf/resolv.conf");
				}
			}
		}
	}
}
