using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Text;
using DokanNet;
using FileAccess = DokanNet.FileAccess;

namespace Force.ChunkFS
{
	internal class DokanOperations : IDokanOperations
	{
		private readonly IFileStorage _storage;

		[DllImport("Kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern  bool GetVolumeInformation(
			string rootPathName,
			StringBuilder volumeNameBuffer,
			int volumeNameSize,
			out uint volumeSerialNumber,
			out uint maximumComponentLength,
			out FileSystemFeatures fileSystemFlags,
			StringBuilder fileSystemNameBuffer,
			int nFileSystemNameSize);

		private readonly string _volumeName = "ChunkFS";

		private readonly string _fsName = "NTFS";

		private FileSystemFeatures? _existingFsFlags;

		public DokanOperations(IFileStorage storage, string mountName)
		{
			_storage = storage;
			// mount to junction point
			if (mountName.Length > 3)
			{
				if (!Directory.Exists(mountName))
					Directory.CreateDirectory(mountName);
				StringBuilder volname = new StringBuilder(261);
				StringBuilder fsname = new StringBuilder(261);
				uint sernum, maxlen;
				FileSystemFeatures flags;
				if(!GetVolumeInformation(mountName.Substring(0, 3), volname, volname.Capacity, out sernum, out maxlen, out flags, fsname, fsname.Capacity))
					Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
				_volumeName = volname.ToString();
				_fsName = fsname.ToString();
				_existingFsFlags = flags;
			}
		}

		#region Implementation of IDokanOperations

		public NtStatus CreateFile(string fileName, FileAccess access, FileShare share, FileMode mode,
			FileOptions options, FileAttributes attributes, DokanFileInfo info)
		{
			// Console.WriteLine("CreateFile: " + fileName + " A:" + access + " S:" + share + " M:" + mode + " O:" + options + " A:" + attributes + " D:" + info.IsDirectory);
			if (info.IsDirectory)
			{
				switch (mode)
				{
					case FileMode.Open:
						return _storage.OpenOrCreateDirectory(fileName, true, false);

					case FileMode.CreateNew:
						return _storage.OpenOrCreateDirectory(fileName, false, true);

					case FileMode.Create:
					case FileMode.OpenOrCreate:
							return _storage.OpenOrCreateDirectory(fileName, true, true);
					default:
						return NtStatus.NotImplemented;
				}
			}

			attributes &= ~FileAttributes.Compressed;
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
			attributes &= ~FileAttributes.Compressed;
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
			volumeLabel = _volumeName;
			fileSystemName = _fsName;

			features = FileSystemFeatures.CasePreservedNames | FileSystemFeatures.CaseSensitiveSearch |
			           FileSystemFeatures.PersistentAcls | FileSystemFeatures.SupportsRemoteStorage |
			           FileSystemFeatures.UnicodeOnDisk;

			// commented. not helps anyway
			// compression, we should set this flag to fix copying errors when attached to mount point
			// if (_existingFsFlags.HasValue && _existingFsFlags.Value.HasFlag((FileSystemFeatures) 0x10))
			//	features |= (FileSystemFeatures) 0x10;

			return DokanResult.Success;
		}

		public NtStatus GetFileSecurity(string fileName, out FileSystemSecurity security, AccessControlSections sections,
			DokanFileInfo info)
		{
			security = _storage.GetFileSecurity(fileName, info);
			// if (security == null)
			//	Console.WriteLine("Null Security: " + fileName + " " + " " + (info.IsDirectory ? "true" : "false"));
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