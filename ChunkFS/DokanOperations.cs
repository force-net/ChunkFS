using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using DokanNet;
using FileAccess = DokanNet.FileAccess;

namespace Force.ChunkFS
{
	internal class DokanOperations : IDokanOperations
	{
		private readonly IFileStorage _storage;


		public DokanOperations(IFileStorage storage)
		{
			_storage = storage;
		}

		#region Implementation of IDokanOperations

		public NtStatus CreateFile(string fileName, FileAccess access, FileShare share, FileMode mode,
			FileOptions options, FileAttributes attributes, DokanFileInfo info)
		{
			if (info.IsDirectory)
			{
				switch (mode)
				{
					case FileMode.Open:
						return _storage.OpenDirectory(fileName);

					case FileMode.CreateNew:
						return _storage.CreateDirectory(fileName);
					default:
						return NtStatus.NotImplemented;
				}
			}

			return _storage.OpenFile(fileName, access, share, mode, options, attributes, info);
		}

		public void Cleanup(string fileName, DokanFileInfo info)
		{
			_storage.CloseFile(fileName, info);
		}

		public void CloseFile(string fileName, DokanFileInfo info)
		{
			_storage.CloseFile(fileName, info);
		}

		public NtStatus ReadFile(string fileName, byte[] buffer, out int bytesRead, long offset, DokanFileInfo info)
		{
			bytesRead = _storage.ReadFile(fileName, buffer, offset, info);
			return NtStatus.Success;
		}

		public NtStatus WriteFile(string fileName, byte[] buffer, out int bytesWritten, long offset, DokanFileInfo info)
		{
			bytesWritten = _storage.WriteFile(fileName, buffer, offset, info);
			return NtStatus.Success;
		}

		public NtStatus FlushFileBuffers(string fileName, DokanFileInfo info)
		{
			return _storage.FlushFile(info);
		}

		public NtStatus GetFileInformation(string fileName, out FileInformation fileInfo, DokanFileInfo info)
		{
			// may be called with info.Context == null, but usually it isn't
			fileInfo = _storage.GetFileInformation(fileName, info);

			return DokanResult.Success;
		}

		public NtStatus FindFiles(string fileName, out IList<FileInformation> files, DokanFileInfo info)
		{
			// This function is not called because FindFilesWithPattern is implemented
			// Return DokanResult.NotImplemented in FindFilesWithPattern to make FindFiles called
			files = _storage.FindFiles(fileName, "*");

			return DokanResult.Success;
		}

		public NtStatus SetFileAttributes(string fileName, FileAttributes attributes, DokanFileInfo info)
		{
			return _storage.SetFileAttributes(fileName, attributes, info);
		}

		public NtStatus SetFileTime(string fileName, DateTime? creationTime, DateTime? lastAccessTime,
			DateTime? lastWriteTime, DokanFileInfo info)
		{
			return _storage.SetFileTime(fileName, creationTime, lastAccessTime, lastWriteTime, info);
		}

		public NtStatus DeleteFile(string fileName, DokanFileInfo info)
		{
			return _storage.CheckCanDeleteFile(fileName);
		}

		public NtStatus DeleteDirectory(string fileName, DokanFileInfo info)
		{
			return _storage.CheckCanDeleteDirectory(fileName);
		}

		public NtStatus MoveFile(string oldName, string newName, bool replace, DokanFileInfo info)
		{
			return _storage.MoveFile(oldName, newName, replace, info);
		}

		public NtStatus SetEndOfFile(string fileName, long length, DokanFileInfo info)
		{
			return _storage.SetLength(length, info);
		}

		public NtStatus SetAllocationSize(string fileName, long length, DokanFileInfo info)
		{
			return _storage.SetLength(length, info);
		}

		public NtStatus LockFile(string fileName, long offset, long length, DokanFileInfo info)
		{
			return _storage.LockFile(offset, length, info);
		}

		public NtStatus UnlockFile(string fileName, long offset, long length, DokanFileInfo info)
		{
			return _storage.UnlockFile(offset, length, info);
		}

		public NtStatus GetDiskFreeSpace(out long freeBytesAvailable, out long totalNumberOfBytes, out long totalNumberOfFreeBytes, DokanFileInfo info)
		{
			var dinfo = _storage.GetDiskFreeSpace();

			freeBytesAvailable = dinfo.TotalFreeSpace;
			totalNumberOfBytes = dinfo.TotalSize;
			totalNumberOfFreeBytes = dinfo.AvailableFreeSpace;
			return NtStatus.Success;
		}

		public NtStatus GetVolumeInformation(out string volumeLabel, out FileSystemFeatures features,
			out string fileSystemName, DokanFileInfo info)
		{
			volumeLabel = "ChunkFS";
			fileSystemName = "NTFS";

			features = FileSystemFeatures.CasePreservedNames | FileSystemFeatures.CaseSensitiveSearch |
			           FileSystemFeatures.PersistentAcls | FileSystemFeatures.SupportsRemoteStorage |
			           FileSystemFeatures.UnicodeOnDisk;

			return DokanResult.Success;
		}

		public NtStatus GetFileSecurity(string fileName, out FileSystemSecurity security, AccessControlSections sections,
			DokanFileInfo info)
		{
			security = _storage.GetFileSecurity(fileName, info);
			if (security == null)
				Console.WriteLine("Null Security: " + fileName + " " + " " + (info.IsDirectory ? "true" : "false"));
			return security == null ? NtStatus.AccessDenied : NtStatus.Success;
		}

		public NtStatus SetFileSecurity(string fileName, FileSystemSecurity security, AccessControlSections sections,
			DokanFileInfo info)
		{
			return _storage.SetFileSecurity(fileName, security, info);
		}

		public NtStatus Mounted(DokanFileInfo info)
		{
			return DokanResult.Success;
		}

		public NtStatus Unmounted(DokanFileInfo info)
		{
			return DokanResult.Success;
		}

		public NtStatus FindStreams(string fileName, IntPtr enumContext, out string streamName, out long streamSize,
			DokanFileInfo info)
		{
			streamName = string.Empty;
			streamSize = 0;
			return DokanResult.NotImplemented;
		}

		public NtStatus FindStreams(string fileName, out IList<FileInformation> streams, DokanFileInfo info)
		{
			streams = new FileInformation[0];
			return DokanResult.NotImplemented;
		}

		public NtStatus FindFilesWithPattern(string fileName, string searchPattern, out IList<FileInformation> files,
			DokanFileInfo info)
		{
			files = _storage.FindFiles(fileName, searchPattern);

			return DokanResult.Success;
		}

		#endregion Implementation of IDokanOperations
	}
}