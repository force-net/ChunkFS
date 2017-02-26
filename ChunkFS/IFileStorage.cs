using System;
using System.IO;
using System.Security.AccessControl;
using DokanNet;
using FileAccess = DokanNet.FileAccess;

namespace Force.ChunkFS
{
	public interface IFileStorage
	{
		NtStatus OpenDirectory(string directoryPath);
		NtStatus CreateDirectory(string directoryPath);

		NtStatus OpenFile(string filePath, FileAccess access, FileShare share, FileMode mode,
			FileOptions options, FileAttributes attributes, DokanFileInfo info);

		void CloseFile(string fileName, DokanFileInfo info);
		int ReadFile(string fileName, byte[] buffer, long offset, DokanFileInfo info);
		int WriteFile(string fileName, byte[] buffer, long offset, DokanFileInfo info);
		NtStatus FlushFile(DokanFileInfo info);
		FileInformation GetFileInformation(string fileName, DokanFileInfo info);
		FileInformation[] FindFiles(string fileName, string searchPattern);
		NtStatus SetFileAttributes(string fileName, FileAttributes attributes, DokanFileInfo info);

		NtStatus SetFileTime(string fileName, DateTime? creationTime, DateTime? lastAccessTime,
			DateTime? lastWriteTime, DokanFileInfo info);

		NtStatus CheckCanDeleteFile(string fileName);
		NtStatus CheckCanDeleteDirectory(string fileName);
		NtStatus MoveFile(string oldName, string newName, bool replace, DokanFileInfo info);
		NtStatus SetLength(long length, DokanFileInfo info);
		NtStatus LockFile(long offset, long length, DokanFileInfo info);
		NtStatus UnlockFile(long offset, long length, DokanFileInfo info);
		DriveInfo GetDiskFreeSpace(string basePath = null);
		FileSystemSecurity GetFileSecurity(string fileName, DokanFileInfo info);
		NtStatus SetFileSecurity(string fileName, FileSystemSecurity security, DokanFileInfo info);
	}
}