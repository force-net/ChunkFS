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

		private readonly IFileStorage _realStorage;

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
			try
			{
				return _realStorage.OpenDirectory(GetPath(directoryPath));
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				throw;
			}
		}

		public NtStatus CreateDirectory(string directoryPath)
		{
			try
			{
				return _realStorage.CreateDirectory(GetPath(directoryPath));
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				throw;
			}
		}

		public NtStatus OpenFile(string filePath, FileAccess access, FileShare share, FileMode mode, FileOptions options,
			FileAttributes attributes, DokanFileInfo info)
		{
			try
			{
				return _realStorage.OpenFile(GetPath(filePath), access, share, mode, options, attributes, info);
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				throw;
			}
		}

		public void CloseFile(string fileName, DokanFileInfo info)
		{
			try
			{
				_realStorage.CloseFile(GetPath(fileName), info);
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				throw;
			}
		}

		public int ReadFile(string fileName, byte[] buffer, long offset, DokanFileInfo info)
		{
			try
			{
				return _realStorage.ReadFile(GetPath(fileName), buffer, offset, info);
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				throw;
			}
		}

		public int WriteFile(string fileName, byte[] buffer, long offset, DokanFileInfo info)
		{
			try
			{
				return _realStorage.WriteFile(GetPath(fileName), buffer, offset, info);
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				throw;
			}
		}

		public NtStatus FlushFile(DokanFileInfo info)
		{
			try
			{
				return _realStorage.FlushFile(info);
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				throw;
			}
		}

		public FileInformation GetFileInformation(string fileName, DokanFileInfo info)
		{
			try
			{
				return _realStorage.GetFileInformation(GetPath(fileName), info);
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				throw;
			}
		}

		public FileInformation[] FindFiles(string fileName, string searchPattern)
		{
			try
			{
				return _realStorage.FindFiles(GetPath(fileName), searchPattern);
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				throw;
			}
		}

		public NtStatus SetFileAttributes(string fileName, FileAttributes attributes, DokanFileInfo info)
		{
			try
			{
				return _realStorage.SetFileAttributes(GetPath(fileName), attributes, info);
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				throw;
			}
		}

		public NtStatus SetFileTime(string fileName, DateTime? creationTime, DateTime? lastAccessTime, DateTime? lastWriteTime,
			DokanFileInfo info)
		{
			try
			{
				return _realStorage.SetFileTime(GetPath(fileName), creationTime, lastAccessTime, lastWriteTime, info);
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				throw;
			}
		}

		public NtStatus CheckCanDeleteFile(string fileName)
		{
			try
			{
				return _realStorage.CheckCanDeleteFile(GetPath(fileName));
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				throw;
			}
		}

		public NtStatus CheckCanDeleteDirectory(string fileName)
		{
			try
			{
				return _realStorage.CheckCanDeleteDirectory(GetPath(fileName));
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				throw;
			}
		}

		public NtStatus MoveFile(string oldName, string newName, bool replace, DokanFileInfo info)
		{
			try
			{
				return _realStorage.MoveFile(GetPath(oldName), GetPath(newName), replace, info);
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				throw;
			}
		}

		public NtStatus SetLength(long length, DokanFileInfo info)
		{
			try
			{
				return _realStorage.SetLength(length, info);
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				throw;
			}
		}

		public NtStatus LockFile(long offset, long length, DokanFileInfo info)
		{
			try
			{
				return _realStorage.LockFile(offset, length, info);
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				throw;
			}
		}

		public NtStatus UnlockFile(long offset, long length, DokanFileInfo info)
		{
			try
			{
				return _realStorage.UnlockFile(offset, length, info);
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				throw;
			}
		}

		public DriveInfo GetDiskFreeSpace(string basePath)
		{
			try
			{
				return _realStorage.GetDiskFreeSpace(_basePath);
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				throw;
			}
		}

		public FileSystemSecurity GetFileSecurity(string fileName, DokanFileInfo info)
		{
			try
			{
				return _realStorage.GetFileSecurity(GetPath(fileName), info);
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				throw;
			}
		}

		public NtStatus SetFileSecurity(string fileName, FileSystemSecurity security, DokanFileInfo info)
		{
			try
			{
				return _realStorage.SetFileSecurity(GetPath(fileName), security, info);
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				throw;
			}
		}
	}
}