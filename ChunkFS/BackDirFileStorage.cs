using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using DokanNet;
using FileAccess = DokanNet.FileAccess;

namespace Force.ChunkFS
{
	public class BackDirFileStorage : IFileStorage
	{
		private const FileAccess DataAccess = FileAccess.ReadData | FileAccess.WriteData | FileAccess.AppendData |
		                                      FileAccess.Execute |
		                                      FileAccess.GenericExecute | FileAccess.GenericWrite |
		                                      FileAccess.GenericRead;

		private const FileAccess DataWriteAccess = FileAccess.WriteData | FileAccess.AppendData |
		                                           FileAccess.Delete |
		                                           FileAccess.GenericWrite;


		public NtStatus OpenDirectory(string directoryPath)
		{
			try
			{
				if (!Directory.Exists(directoryPath))
				{
					try
					{
						if (!File.GetAttributes(directoryPath).HasFlag(FileAttributes.Directory))
							return NtStatus.NotADirectory;
					}
					catch (Exception)
					{
						return DokanResult.FileNotFound;
					}
					return DokanResult.PathNotFound;
				}

				// ReSharper disable once ReturnValueOfPureMethodIsNotUsed
				new DirectoryInfo(directoryPath).EnumerateFileSystemInfos().Any();
			}
			catch (UnauthorizedAccessException)
			{
				return DokanResult.AccessDenied;
			}

			return NtStatus.Success;
		}

		public NtStatus CreateDirectory(string directoryPath)
		{
			try
			{
				if (Directory.Exists(directoryPath))
					return DokanResult.FileExists;
				try
				{
					if (File.GetAttributes(directoryPath).HasFlag(FileAttributes.Directory))
						return DokanResult.AlreadyExists;
				}
				catch (IOException)
				{
				}

				Directory.CreateDirectory(directoryPath);
			}
			catch (UnauthorizedAccessException)
			{
				return DokanResult.AccessDenied;
			}

			return NtStatus.Success;
		}

		public NtStatus OpenFile(string filePath, FileAccess access, FileShare share, FileMode mode,
			FileOptions options, FileAttributes attributes, DokanFileInfo info)
		{
			// if (filePath != "\\")
			//	Console.WriteLine("OpenFile: " + filePath /*+ " " + access + " " + share + " " + mode + " " + options + " " + attributes*/ + " " + info.Context);

			var pathExists = true;
			var pathIsDirectory = false;

			var readWriteAttributes = (access & DataAccess) == 0;
			var readAccess = (access & DataWriteAccess) == 0;

			var directoryExist = Directory.Exists(filePath);

			try
			{

				pathExists = directoryExist || new ChunkedFileInfo(filePath).Exists();
				pathIsDirectory = directoryExist;
			}
			catch (IOException)
			{
			}

			switch (mode)
			{
				case FileMode.Open:
					if (pathExists)
					{
						if (readWriteAttributes || pathIsDirectory)
							// check if driver only wants to read attributes, security info, or open directory
						{
							if (pathIsDirectory && (access & FileAccess.Delete) == FileAccess.Delete
							    && (access & FileAccess.Synchronize) != FileAccess.Synchronize)
								//It is a DeleteFile request on a directory
								return DokanResult.AccessDenied;

							info.IsDirectory = pathIsDirectory;
							info.Context = new object();
							// must set it to someting if you return DokanError.Success

							return DokanResult.Success;
						}
					}
					else
					{
						return DokanResult.FileNotFound;
					}
					break;

				case FileMode.CreateNew:
					if (pathExists)
						return DokanResult.FileExists;
					break;

				case FileMode.Truncate:
					if (!pathExists)
						return DokanResult.FileNotFound;
					break;
			}

			try
			{
				var result = DokanResult.Success;
				info.Context = new ChunkedFileInfo(filePath, readAccess ? System.IO.FileAccess.Read : System.IO.FileAccess.ReadWrite, mode/*, share, 4096, options*/);

				if (pathExists && (mode == FileMode.OpenOrCreate || mode == FileMode.Create))
					result = DokanResult.AlreadyExists;

				if (mode == FileMode.CreateNew || mode == FileMode.Create) //Files are always created as Archive
					attributes |= FileAttributes.Archive;
				((ChunkedFileInfo)info.Context).Attributes = attributes;
				return result;
			}
			catch (UnauthorizedAccessException) // don't have access rights
			{
				return DokanResult.AccessDenied;
			}
			catch (DirectoryNotFoundException)
			{
				return DokanResult.PathNotFound;
			}
			catch (Exception ex)
			{
				var hr = (uint) Marshal.GetHRForException(ex);
				switch (hr)
				{
					case 0x80070020: //Sharing violation
						return DokanResult.SharingViolation;
					default:
						throw;
				}
			}
		}

		public void CloseFile(string fileName, DokanFileInfo info)
		{
			(info.Context as ChunkedFileInfo)?.Dispose();
			info.Context = null;

			if (info.DeleteOnClose)
			{
				if (info.IsDirectory)
				{
					Directory.Delete(fileName);
				}
				else
				{
					new ChunkedFileInfo(fileName).DeleteFile();
				}
			}
		}

		public int ReadFile(string fileName, byte[] buffer, long offset, DokanFileInfo info)
		{
			if (info.Context == null) // memory mapped read
			{
				using (var stream = new ChunkedFileInfo(fileName))
				{
					return stream.Read(buffer, offset);
				}
			}
			else // normal read
			{
				var stream = info.Context as ChunkedFileInfo;
				if (stream != null)
				{
					lock (stream) //Protect from overlapped read
					{
						return stream.Read(buffer, offset);
					}
				}
			}

			// incorrect situation
			return 0;
		}

		public int WriteFile(string fileName, byte[] buffer, long offset, DokanFileInfo info)
		{
			// Console.WriteLine("Write: " + fileName + " offset " + offset + " length " + buffer.Length);
			if (info.Context == null)
			{
				using (var stream = new ChunkedFileInfo(fileName, System.IO.FileAccess.Write, FileMode.Open))
				{
					stream.Write(buffer, offset);
					return buffer.Length;
				}
			}
			else
			{
				var stream = info.Context as ChunkedFileInfo;
				if (stream != null)
				{
					lock (stream) //Protect from overlapped write
					{
						stream.Write(buffer, offset);
					}

					return buffer.Length;
				}
			}

			// incorrect situation
			return 0;
		}

		public NtStatus FlushFile(DokanFileInfo info)
		{
			try
			{
				(info.Context as ChunkedFileInfo)?.Flush();
				return DokanResult.Success;
			}
			catch (IOException)
			{
				return DokanResult.DiskFull;
			}
		}

		public FileInformation GetFileInformation(string fileName, DokanFileInfo info)
		{
			try
			{
				var chunkedInfo = info.Context as ChunkedFileInfo;
				if (chunkedInfo != null)
				{
					return chunkedInfo.GetFileInfo();
				}

				if (Directory.Exists(fileName))
				{
					var dinfo = new DirectoryInfo(fileName);
					return new FileInformation
					{
						Attributes = dinfo.Attributes,
						CreationTime = dinfo.CreationTime,
						LastAccessTime = dinfo.LastAccessTime,
						LastWriteTime = dinfo.LastWriteTime,
						Length = 0,
						FileName = dinfo.Name
					};
				}

				return new ChunkedFileInfo(fileName).GetFileInfo();
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				throw;
			}
		}

		public FileInformation[] FindFiles(string fileName, string searchPattern)
		{
			var directoryInfo = new DirectoryInfo(fileName);
			var dirs = directoryInfo.GetDirectories(searchPattern)
				.Select(x => new FileInformation
				{
					Attributes = x.Attributes,
					CreationTime = x.CreationTime,
					LastAccessTime = x.LastAccessTime,
					LastWriteTime = x.LastWriteTime,
					Length = 0,
					FileName = x.Name
				});

			var files = dirs.Concat(directoryInfo
				.GetFileSystemInfos(searchPattern + ChunkFSConstants.ChunkExtension + "*")
				.GroupBy(x => Path.GetFileNameWithoutExtension(x.Name))
				.Select(x =>
				{
					var finfo = x.FirstOrDefault(y => y.Name.EndsWith(ChunkFSConstants.MetaFileExtension));
					// some failed data
					if (finfo == null)
						return new FileInformation();
					return new FileInformation
					{
						Attributes = finfo.Attributes,
						CreationTime = finfo.CreationTime,
						LastAccessTime = finfo.LastAccessTime,
						LastWriteTime = finfo.LastWriteTime,
						Length = x.Sum(y => ((FileInfo) y).Length),
						FileName = Path.GetFileNameWithoutExtension(finfo.Name)
					};
				})).Where(x => x.FileName != null).ToArray();

			return files;
		}

		public NtStatus SetFileAttributes(string fileName, FileAttributes attributes, DokanFileInfo info)
		{
			try
			{
				ChunkedFileInfo c = info.Context as ChunkedFileInfo;
				if (c != null)
					c.Attributes = attributes;
				else
				{
					if (Directory.Exists(fileName))
						File.SetAttributes(fileName, attributes);
					else
					{
						using (var chunkedFileInfo = new ChunkedFileInfo(fileName, System.IO.FileAccess.Read, FileMode.Open))
							chunkedFileInfo.Attributes = attributes;
					}
				}
				return DokanResult.Success;
			}
			catch (UnauthorizedAccessException)
			{
				return DokanResult.AccessDenied;
			}
			catch (FileNotFoundException)
			{
				return DokanResult.FileNotFound;
			}
			catch (DirectoryNotFoundException)
			{
				return DokanResult.PathNotFound;
			}
		}

		public NtStatus SetFileTime(string fileName, DateTime? creationTime, DateTime? lastAccessTime,
			DateTime? lastWriteTime, DokanFileInfo info)
		{
			try
			{
				var doCloseFile = true;
				var fs = info.Context as ChunkedFileInfo;
				if (fs == null)
				{
					if (Directory.Exists(fileName))
					{
						if (creationTime.HasValue)
							Directory.SetCreationTime(fileName, creationTime.Value);

						if (lastAccessTime.HasValue)
							Directory.SetLastAccessTime(fileName, lastAccessTime.Value);

						if (lastWriteTime.HasValue)
							Directory.SetLastWriteTime(fileName, lastWriteTime.Value);
						return NtStatus.Success;
					}

					fs = new ChunkedFileInfo(fileName);
					doCloseFile = false;
				}

				if (creationTime.HasValue)
					fs.CreationTime = creationTime;

				if (lastAccessTime.HasValue)
					fs.LastAccessTime = lastAccessTime;

				if (lastWriteTime.HasValue)
					fs.LastWriteTime = lastAccessTime;

				if (doCloseFile)
					fs.Close();

				return DokanResult.Success;
			}
			catch (UnauthorizedAccessException)
			{
				return DokanResult.AccessDenied;
			}
			catch (FileNotFoundException)
			{
				return DokanResult.FileNotFound;
			}
			catch (DirectoryNotFoundException)
			{
				return DokanResult.PathNotFound;
			}
			catch (Exception)
			{
				return DokanResult.AccessDenied;
			}
		}

		public NtStatus CheckCanDeleteFile(string fileName)
		{
			// we just check here if we could delete the file - the true deletion is in Cleanup
			if (Directory.Exists(fileName))
				return DokanResult.AccessDenied;

			if (!new ChunkedFileInfo(fileName).Exists())
				return DokanResult.FileNotFound;

			if (new ChunkedFileInfo(fileName).GetAttributes().HasFlag(FileAttributes.Directory))
				return DokanResult.AccessDenied;

			return DokanResult.Success;
		}

		public NtStatus CheckCanDeleteDirectory(string fileName)
		{
			return Directory.EnumerateFileSystemEntries(fileName).Any()
				? DokanResult.DirectoryNotEmpty
				: DokanResult.Success;
		}

		public NtStatus MoveFile(string oldName, string newName, bool replace, DokanFileInfo info)
		{
			(info.Context as ChunkedFileInfo)?.Dispose();
			info.Context = null;

			var exist = info.IsDirectory ? Directory.Exists(newName) : File.Exists(newName);

			try
			{
				if (!exist)
				{
					info.Context = null;
					if (info.IsDirectory)
						Directory.Move(oldName, newName);
					else
						new ChunkedFileInfo(oldName).MoveFile(newName);
					return DokanResult.Success;
				}
				else if (replace)
				{
					info.Context = null;

					if (info.IsDirectory) //Cannot replace directory destination - See MOVEFILE_REPLACE_EXISTING
						return DokanResult.AccessDenied;

					new ChunkedFileInfo(oldName).DeleteFile();
					new ChunkedFileInfo(oldName).MoveFile(newName);
					return DokanResult.Success;
				}
			}
			catch (UnauthorizedAccessException)
			{
				return DokanResult.AccessDenied;
			}

			return DokanResult.FileExists;
		}

		public NtStatus SetLength(long length, DokanFileInfo info)
		{
			try
			{
				((ChunkedFileInfo)info.Context)?.SetLength(length);
				return DokanResult.Success;
			}
			catch (IOException)
			{
				return DokanResult.DiskFull;
			}
		}

		public NtStatus LockFile(long offset, long length, DokanFileInfo info)
		{
			try
			{
				// TODO: add locks
				// ((FileStream) info.Context)?.Lock(offset, length);
				return DokanResult.Success;
			}
			catch (IOException)
			{
				return DokanResult.AccessDenied;
			}
		}

		public NtStatus UnlockFile(long offset, long length, DokanFileInfo info)
		{
			try
			{
				// TODO: add locks
				// ((FileStream) info.Context)?.Unlock(offset, length);
				return DokanResult.Success;
			}
			catch (IOException)
			{
				return DokanResult.AccessDenied;
			}
		}

		public DriveInfo GetDiskFreeSpace(string basePath)
		{
			var dinfo = DriveInfo.GetDrives().Single(di => string.Equals(di.RootDirectory.Name, Path.GetPathRoot(basePath + "\\"), StringComparison.OrdinalIgnoreCase));
			return dinfo;
		}

		public FileSystemSecurity GetFileSecurity(string fileName, DokanFileInfo info)
		{
			try
			{
				return info.IsDirectory
					? (FileSystemSecurity) Directory.GetAccessControl(fileName)
					: new ChunkedFileInfo(fileName).GetAccessControl();
			}
			catch (UnauthorizedAccessException)
			{
				return null;
			}
		}

		public NtStatus SetFileSecurity(string fileName, FileSystemSecurity security, DokanFileInfo info)
		{
			try
			{
				if (info.IsDirectory)
				{
					Directory.SetAccessControl(fileName, (DirectorySecurity) security);
				}
				else
				{
					new ChunkedFileInfo(fileName).SetAccessControl((FileSecurity)security);
				}

				return DokanResult.Success;
			}
			catch (UnauthorizedAccessException)
			{
				return DokanResult.AccessDenied;
			}
		}
	}
}