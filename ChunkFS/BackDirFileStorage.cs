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


		public NtStatus OpenOrCreateDirectory(string directoryPath, bool isOpen, bool isCreate)
		{
			try
			{
				var exists = Directory.Exists(directoryPath);
				if (exists)
				{
					if (!isOpen)
						return DokanResult.FileExists;
					try
					{
						// ReSharper disable once ReturnValueOfPureMethodIsNotUsed
						new DirectoryInfo(directoryPath).EnumerateFileSystemInfos().Any();
					}
					catch (UnauthorizedAccessException)
					{
						return DokanResult.AccessDenied;
					}
				}
				else
				{
					if (File.Exists(directoryPath))
						return NtStatus.NotADirectory;
					if (!isCreate)
						return DokanResult.PathNotFound;
					try
					{
						Directory.CreateDirectory(directoryPath);
					}
					catch (UnauthorizedAccessException)
					{
						return DokanResult.AccessDenied;
					}
				}
			}
			catch (IOException)
			{
				return DokanResult.AccessDenied;
			}

			return NtStatus.Success;
		}

		public NtStatus OpenFile(string filePath, FileAccess access, FileShare share, FileMode mode,
			FileOptions options, FileAttributes attributes, DokanFileInfo info)
		{
			// if (filePath != "\\" && !info.IsDirectory)
			//	Console.WriteLine("OpenFile: " + filePath + " A:" + access + " S:" + share + " M:" + mode + " O:" + options + " A:" + attributes + " " + info.Context);

			var pathExists = true;
			var pathIsDirectory = false;

			var readWriteAttributes = (access & DataAccess) == 0;
			var readAccess = (access & DataWriteAccess) == 0;

			var directoryExist = Directory.Exists(filePath);

			try
			{
				pathExists = directoryExist || ChunkedFileInfo.Exists(filePath);
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

							// if (pathIsDirectory)
							{
								info.IsDirectory = pathIsDirectory;
								// must set it to someting if you return DokanError.Success
								info.Context = new object();
							}

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
				info.Context = new ChunkedFileInfo(filePath, mode, !readAccess);

				if (pathExists && (mode == FileMode.OpenOrCreate || mode == FileMode.Create))
					result = DokanResult.AlreadyExists;

				if (mode == FileMode.CreateNew || mode == FileMode.Create) //Files are always created as Archive
					attributes |= FileAttributes.Archive;

				Console.WriteLine("OpenFIle " + filePath + " attributes: " + attributes);
				if (attributes != 0)
					ChunkedFileInfo.SetAttributes(filePath, attributes);
				// Console.WriteLine("OpenFile result " + filePath + " " + result);
				return result;
			}
			catch (UnauthorizedAccessException) // don't have access rights
			{
				Console.WriteLine("E12");
				return DokanResult.AccessDenied;
			}
			catch (DirectoryNotFoundException)
			{
				Console.WriteLine("E13");
				return DokanResult.PathNotFound;
			}
			catch (Exception ex)
			{
				Console.WriteLine("E14");
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
			// if (!info.IsDirectory)
			//	Console.WriteLine("Close: " + fileName + " Do Delete? " + info.DeleteOnClose);
			(info.Context as ChunkedFileInfo)?.Dispose();
			info.Context = null;

			if (info.DeleteOnClose)
			{
				if (info.IsDirectory)
				{
					if (Directory.Exists(fileName))
						Directory.Delete(fileName);
				}
				else
				{
					ChunkedFileInfo.DeleteFile(fileName);
				}
			}
		}

		public int ReadFile(string fileName, byte[] buffer, long offset, DokanFileInfo info)
		{
			Console.WriteLine("Read: " + fileName + " offset " + offset + " length " + buffer.Length);
			if (info.Context == null) // memory mapped read
			{
				using (var stream = new ChunkedFileInfo(fileName, FileMode.Open, false))
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
			Console.WriteLine("Write: " + fileName + " offset " + offset + " length " + buffer.Length);
			if (info.Context == null)
			{
				using (var stream = new ChunkedFileInfo(fileName, FileMode.Open, true))
				{
					stream.Write(buffer, offset);
					return buffer.Length;
				}
			}
			else
			{
				var stream = info.Context as ChunkedFileInfo;
				stream?.Write(buffer, offset);
				return buffer.Length;
			}

			// incorrect situation
			return 0;
		}

		public NtStatus FlushFile(DokanFileInfo info)
		{
			try
			{
				Console.WriteLine("Flush");
				(info.Context as ChunkedFileInfo)?.Flush();
				return DokanResult.Success;
			}
			catch (IOException)
			{
				Console.WriteLine("Flush fail");
				return DokanResult.DiskFull;
			}
		}

		public FileInformation GetFileInformation(string fileName, DokanFileInfo info)
		{
			// Console.WriteLine("GetFileInfo: " + fileName + " " + (info.Context is ChunkedFileInfo ? "true" : "false"));
			try
			{
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

				return ChunkedFileInfo.GetFileInfo(fileName);
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				throw;
			}
		}

		private static readonly char[] InvalidSearchChars = Path.GetInvalidFileNameChars().Except(new[] {'*', '?'}).ToArray();

		public FileInformation[] FindFiles(string fileName, string searchPattern)
		{
			// Console.WriteLine("FindFiles: " + fileName + " " + searchPattern);
			// rare case, but it is bad to throw an exception here
			if (searchPattern.Any(x => InvalidSearchChars.Contains(x)))
				searchPattern = new string(searchPattern.Select(x => InvalidSearchChars.Contains(x) ? '_' : x).ToArray());
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
				.GetFileSystemInfos(searchPattern + ChunkFSConstants.MetaFileExtension)
				.Select(x =>
				{
					var origName = Path.Combine(fileName, Path.GetFileNameWithoutExtension(x.FullName));
					// Console.WriteLine(origName);
					try
					{
						return ChunkedFileInfo.GetFileInfo(origName);
					}
					catch (Exception e)
					{
						Console.WriteLine(e);
						throw;
					}

				})).ToArray();

			// Console.WriteLine(files.Length);
			return files;
		}

		public NtStatus SetFileAttributes(string fileName, FileAttributes attributes, DokanFileInfo info)
		{
			Console.WriteLine("SetFileAttributes: " + fileName + " " + attributes + " " + (info.Context is ChunkedFileInfo ? "true" : "false"));
			try
			{
				if (attributes != 0)
				{
					if (Directory.Exists(fileName))
						File.SetAttributes(fileName, attributes);
					else
					{
						ChunkedFileInfo.SetAttributes(fileName, attributes);
					}
				}

				return DokanResult.Success;
			}
			catch (UnauthorizedAccessException)
			{
				Console.WriteLine("E9");
				return DokanResult.AccessDenied;
			}
			catch (FileNotFoundException)
			{
				Console.WriteLine("E10");
				return DokanResult.FileNotFound;
			}
			catch (DirectoryNotFoundException)
			{
				Console.WriteLine("E11");
				return DokanResult.PathNotFound;
			}
		}

		public NtStatus SetFileTime(string fileName, DateTime? creationTime, DateTime? lastAccessTime,
			DateTime? lastWriteTime, DokanFileInfo info)
		{
			Console.WriteLine("SetFileTime: " + fileName + " " + (info.Context is ChunkedFileInfo ? "true" : "false"));
			try
			{
				var doCloseFile = false;
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

					fs = new ChunkedFileInfo(fileName, FileMode.Open, false);
					doCloseFile = true;
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
				Console.WriteLine("E1");
				return DokanResult.AccessDenied;
			}
			catch (FileNotFoundException)
			{
				Console.WriteLine("E2");
				return DokanResult.FileNotFound;
			}
			catch (DirectoryNotFoundException)
			{
				Console.WriteLine("E3");
				return DokanResult.PathNotFound;
			}
			catch (Exception)
			{
				Console.WriteLine("E4");
				return DokanResult.AccessDenied;
			}
		}

		public NtStatus CheckCanDeleteFile(string fileName)
		{
			Console.WriteLine("Can Delete? " + fileName);
			// we just check here if we could delete the file - the true deletion is in Cleanup
			if (Directory.Exists(fileName))
				return DokanResult.AccessDenied;

			if (!ChunkedFileInfo.Exists(fileName))
				return DokanResult.FileNotFound;

			if (ChunkedFileInfo.GetAttributes(fileName).HasFlag(FileAttributes.Directory))
				return DokanResult.AccessDenied;

			Console.WriteLine("Can Delete+ " + fileName);
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
			Console.WriteLine("Move: " + oldName + " -> " + newName + ". Replace: " + replace);
			(info.Context as ChunkedFileInfo)?.Dispose();
			info.Context = null;

			var exist = info.IsDirectory ? Directory.Exists(newName) : ChunkedFileInfo.Exists(newName);

			try
			{
				if (!exist)
				{
					info.Context = null;
					if (info.IsDirectory)
						Directory.Move(oldName, newName);
					else
						ChunkedFileInfo.MoveFile(oldName, newName);
					return DokanResult.Success;
				}
				else if (replace)
				{
					info.Context = null;

					if (info.IsDirectory) //Cannot replace directory destination - See MOVEFILE_REPLACE_EXISTING
						return DokanResult.AccessDenied;

					ChunkedFileInfo.DeleteFile(oldName);
					ChunkedFileInfo.MoveFile(oldName, newName);
					return DokanResult.Success;
				}
				else
				{
					// just change case. we cannot do that in normal way, so, lets do this in hard way
					if (string.Equals(oldName, newName, StringComparison.InvariantCultureIgnoreCase))
					{
						// todo: add support for directory
						if (!info.IsDirectory)
						{
							var tmpName = oldName + Guid.NewGuid().ToString("n");
							ChunkedFileInfo.MoveFile(oldName, tmpName);
							ChunkedFileInfo.MoveFile(tmpName, newName);
						}

						return DokanResult.Success;
					}
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
				Console.WriteLine("Set Length " + length);
				((ChunkedFileInfo)info.Context)?.SetLength(length);
				return DokanResult.Success;
			}
			catch (IOException)
			{
				Console.WriteLine("E6 " + length);
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
			Console.WriteLine("GetFileSecurity: " + fileName + " " + " " + (info.IsDirectory ? "true" : "false"));
			try
			{
				return info.IsDirectory
					? (FileSystemSecurity) Directory.GetAccessControl(fileName)
					: ChunkedFileInfo.GetAccessControl(fileName);
			}
			catch (UnauthorizedAccessException)
			{
				Console.WriteLine("E7");
				return null;
			}
		}

		public NtStatus SetFileSecurity(string fileName, FileSystemSecurity security, DokanFileInfo info)
		{
			// Console.WriteLine("SetFileSecurity: " + fileName + " " + " " + (info.IsDirectory ? "true" : "false"));
			try
			{
				if (info.IsDirectory)
				{
					Directory.SetAccessControl(fileName, (DirectorySecurity) security);
				}
				else
				{
					ChunkedFileInfo.SetAccessControl(fileName, (FileSecurity)security);
				}

				return DokanResult.Success;
			}
			catch (UnauthorizedAccessException)
			{
				Console.WriteLine("E8");
				return DokanResult.AccessDenied;
			}
		}
	}
}