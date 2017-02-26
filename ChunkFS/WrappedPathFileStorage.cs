using System;
using System.IO;
using System.Security.AccessControl;
using DokanNet;
using FileAccess = DokanNet.FileAccess;

namespace Force.ChunkFS
{
	public class WrappedPathFileStorage : IFileStorage
	{
		private readonly string _basePath;

		private IFileStorage _realStorage;

		public WrappedPathFileStorage(IFileStorage realStorage, string path)
		{
			if (!Directory.Exists(path))
				throw new ArgumentException(nameof(path));
			_basePath = path;
			_realStorage = realStorage;
		}

		public string GetPath(string path)
		{
			return _basePath + path;
		}

		public NtStatus OpenDirectory(string directoryPath)
		{
			return _realStorage.OpenDirectory(GetPath(directoryPath));
		}

		public NtStatus CreateDirectory(string directoryPath)
		{
			return _realStorage.CreateDirectory(GetPath(directoryPath));
		}

		public NtStatus OpenFile(string filePath, FileAccess access, FileShare share, FileMode mode, FileOptions options,
			FileAttributes attributes, DokanFileInfo info)
		{
			return _realStorage.OpenFile(GetPath(filePath), access, share, mode, options, attributes, info);
		}

		public void CloseFile(string fileName, DokanFileInfo info)
		{
			_realStorage.CloseFile(GetPath(fileName), info);
		}

		public int ReadFile(string fileName, byte[] buffer, long offset, DokanFileInfo info)
		{
			return _realStorage.ReadFile(GetPath(fileName), buffer, offset, info);
		}

		public int WriteFile(string fileName, byte[] buffer, long offset, DokanFileInfo info)
		{
			return _realStorage.WriteFile(GetPath(fileName), buffer, offset, info);
		}

		public NtStatus FlushFile(DokanFileInfo info)
		{
			return _realStorage.FlushFile(info);
		}

		public FileInformation GetFileInformation(string fileName, DokanFileInfo info)
		{
			return _realStorage.GetFileInformation(GetPath(fileName), info);
		}

		public FileInformation[] FindFiles(string fileName, string searchPattern)
		{
			return _realStorage.FindFiles(GetPath(fileName), searchPattern);
		}

		public NtStatus SetFileAttributes(string fileName, FileAttributes attributes, DokanFileInfo info)
		{
			return _realStorage.SetFileAttributes(GetPath(fileName), attributes, info);
		}

		public NtStatus SetFileTime(string fileName, DateTime? creationTime, DateTime? lastAccessTime, DateTime? lastWriteTime,
			DokanFileInfo info)
		{
			return _realStorage.SetFileTime(GetPath(fileName), creationTime, lastAccessTime, lastWriteTime, info);
		}

		public NtStatus CheckCanDeleteFile(string fileName)
		{
			return _realStorage.CheckCanDeleteFile(GetPath(fileName));
		}

		public NtStatus CheckCanDeleteDirectory(string fileName)
		{
			return _realStorage.CheckCanDeleteDirectory(GetPath(fileName));
		}

		public NtStatus MoveFile(string oldName, string newName, bool replace, DokanFileInfo info)
		{
			return _realStorage.MoveFile(GetPath(oldName), GetPath(newName), replace, info);
		}

		public NtStatus SetLength(long length, DokanFileInfo info)
		{
			return _realStorage.SetLength(length, info);
		}

		public NtStatus LockFile(long offset, long length, DokanFileInfo info)
		{
			return _realStorage.LockFile(offset, length, info);
		}

		public NtStatus UnlockFile(long offset, long length, DokanFileInfo info)
		{
			return _realStorage.UnlockFile(offset, length, info);
		}

		public DriveInfo GetDiskFreeSpace(string basePath)
		{
			return _realStorage.GetDiskFreeSpace(_basePath);
		}

		public FileSystemSecurity GetFileSecurity(string fileName, DokanFileInfo info)
		{
			return _realStorage.GetFileSecurity(GetPath(fileName), info);
		}

		public NtStatus SetFileSecurity(string fileName, FileSystemSecurity security, DokanFileInfo info)
		{
			return _realStorage.SetFileSecurity(GetPath(fileName), security, info);
		}
	}
}