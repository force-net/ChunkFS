using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using DokanNet;
using FileAccess = System.IO.FileAccess;

namespace Force.ChunkFS
{
	public class ChunkedFileInfo : IDisposable
	{
		private readonly string _filePrefix;

		private readonly string _directory;

		private readonly string _fileFullPath;

		private Stream _currentReadStream;

		private long _currentReadIndex = -1;

		private Stream _currentWriteStream;

		private long _currentWriteIndex = -1;

		private string _currentWriteStreamFileName;

		private readonly bool _isWriteAccess;

		public ChunkedFileInfo(string fileName, FileMode mode, bool isWriteAccess)
		{
			_filePrefix = Path.GetFileName(fileName);
			_directory = Path.GetDirectoryName(fileName);
			// meta file name with chunk suffix for better search and to ensure used won't try to open empty file by extension
			_fileFullPath = fileName + ChunkFSConstants.MetaFileExtension;
			if (mode == FileMode.Truncate || mode == FileMode.Create)
				DeleteAllChunks(_directory, _filePrefix);
			// Console.WriteLine(fileName + " " + mode);
			if (isWriteAccess)
			{
				if ((mode == FileMode.Create || mode == FileMode.OpenOrCreate || mode == FileMode.Append || mode == FileMode.CreateNew))
					CreateRealFile();
				else
				{
					// ensuring correct case for file (especially for chunks)
					_filePrefix = GetFileInfo(fileName).FileName;
				}
			}


			_isWriteAccess = isWriteAccess;
		}

		private void CreateRealFile()
		{
			if (!File.Exists(_fileFullPath))
				File.Create(_fileFullPath).Close();
		}


		public DateTime? CreationTime { get; set; }
		public DateTime? LastAccessTime { get; set; }
		public DateTime? LastWriteTime { get; set; }

		private static void DeleteAllChunks(string directory, string fileName)
		{
			GetExistingFiles(directory, fileName).ForEach(File.Delete);
		}

		private static List<string> GetExistingFiles(string directory, string fileName)
		{
			// does not return meta chunk
			return Directory.GetFiles(directory, fileName + ChunkFSConstants.ChunkExtension + "*").Where(x => !x.EndsWith(ChunkFSConstants.MetaFileExtension))
				// .Concat(Directory.GetFiles(_directory, _filePrefix + ChunkFSConstants.ChunkExtension + "*" + ChunkFSConstants.CompressedFileExtension))
				.ToList();
		}

		public void Write(byte[] buffer, long position)
		{
			if (!_isWriteAccess)
				throw new InvalidOperationException("File is opened for read access");
			// position from end
			if (position < 0)
			{
				if (!_length.HasValue) _length = GetLength(Path.Combine(_directory, _filePrefix));
				position = _length.GetValueOrDefault() + 1 + position;
			}

			var offset = 0;
			var count = buffer.Length;
			while (count > 0)
			{
				var requiredChunk = position / ChunkFSConstants.ChunkSize;
				OpenWriteChunk(requiredChunk);
				var chunkOffset = (int) (position % ChunkFSConstants.ChunkSize);
				_currentWriteStream.Position = chunkOffset;

				var toWrite = Math.Min(Math.Min(ChunkFSConstants.ChunkSize, count), ChunkFSConstants.ChunkSize - chunkOffset);
				_currentWriteStream.Write(buffer, offset, toWrite);
				offset += toWrite;
				count -= toWrite;
				position += toWrite;
			}

			if (_length.HasValue && position > _length.Value)
				_length = position;
		}

		public int Read(byte[] buffer, long position)
		{
			// position from end
			if (position < 0)
			{
				if (!_length.HasValue) _length = GetLength(Path.Combine(_directory, _filePrefix));
				position = _length.GetValueOrDefault() + 1 - position;
			}

			var offset = 0;
			var count = buffer.Length;
			while (offset < buffer.Length)
			{
				var requiredChunk = position / ChunkFSConstants.ChunkSize;
				OpenReadChunk(requiredChunk);
				var chunkOffset = position % ChunkFSConstants.ChunkSize;
				_currentReadStream.Position = chunkOffset;

				var toRead = (int)Math.Min(Math.Min(ChunkFSConstants.ChunkSize, count), ChunkFSConstants.ChunkSize - chunkOffset);
				var readed = _currentReadStream.Read(buffer, offset, toRead);
				if (readed == 0)
					break;
				offset += readed;
				count -= readed;
				position += readed;
			}

			return offset;
		}

		private void OpenReadChunk(long requiredChunk)
		{
			if (_currentReadIndex != requiredChunk || _currentReadStream == null)
			{
				_currentReadStream?.Dispose();
				var path = Path.Combine(_directory, _filePrefix + ChunkFSConstants.ChunkExtension + requiredChunk.ToString("0000", CultureInfo.InvariantCulture));
				if (File.Exists(path))
					_currentReadStream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
				else
				{
					// compressed data
					_currentReadStream = CompressorQueue.Instance.DecompressFile(path);
				}
				_currentReadIndex = requiredChunk;
			}
		}

		private void OpenWriteChunk(long requiredChunk)
		{
			if (_currentWriteIndex != requiredChunk || _currentWriteStream == null)
			{
				// will set data after our real close
				_currentWriteStream?.Flush();
				_currentWriteStream?.Dispose();
				if (_currentWriteStreamFileName != null)
					CompressorQueue.Instance.AddFileToCompressQueue(_currentWriteStreamFileName);

				_currentWriteStreamFileName = Path.Combine(_directory, _filePrefix + ChunkFSConstants.ChunkExtension + requiredChunk.ToString("0000", CultureInfo.InvariantCulture));

				if (File.Exists(_currentWriteStreamFileName + ChunkFSConstants.CompressedFileExtension) &&
				    !File.Exists(_currentWriteStreamFileName))
				{
					// we already read this chunk, will reopen it
					if (_currentReadStream != null && _currentReadIndex == _currentWriteIndex)
					{
						// flushing all memory data to uncompressed file
						if (_currentReadStream is MemoryStream)
						{
							_currentReadStream.Position = 0;
							using (var f = File.OpenWrite(_currentWriteStreamFileName))
								_currentReadStream.CopyTo(f);
							CompressorQueue.Instance.DeleteCompressedFile(_currentWriteStreamFileName);
							// reopening read stream
							_currentReadStream = null;
							OpenReadChunk(_currentReadIndex);
						}
					}
					else
					{
						// just uncompress it
						var df = CompressorQueue.Instance.DecompressFile(_currentWriteStreamFileName);
						using (var f = File.OpenWrite(_currentWriteStreamFileName))
							df.CopyTo(f);
						CompressorQueue.Instance.DeleteCompressedFile(_currentWriteStreamFileName);
					}
				}

				_currentWriteStream = new FileStream(_currentWriteStreamFileName, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
				_currentWriteIndex = requiredChunk;
			}
		}

		public void Close()
		{
			_currentReadStream?.Dispose();
			_currentReadStream = null;

			_currentWriteStream?.Dispose();
			_currentWriteStream = null;
			if (_currentWriteStreamFileName != null)
				CompressorQueue.Instance.AddFileToCompressQueue(_currentWriteStreamFileName);

			if (!File.Exists(_fileFullPath))
				return;

			if (CreationTime != null)
				File.SetCreationTime(_fileFullPath, CreationTime.Value);
			if (LastAccessTime != null)
				File.SetLastAccessTime(_fileFullPath, LastAccessTime.Value);
			if (LastWriteTime != null)
				File.SetLastWriteTime(_fileFullPath, LastWriteTime.Value);
		}

		public void Dispose()
		{
			Close();
		}

		public void Flush()
		{
			_currentWriteStream?.Flush();
		}

		public static void MoveFile(string oldPath, string newPath)
		{
			File.Move(oldPath + ChunkFSConstants.MetaFileExtension, newPath + ChunkFSConstants.MetaFileExtension);
			GetExistingFiles(Path.GetDirectoryName(oldPath), Path.GetFileName(oldPath)).ForEach(x =>
			{
				var idx = x.IndexOf(ChunkFSConstants.ChunkExtension, StringComparison.Ordinal);
				// .chunk001 or .chunk002.blz
				var chunkName = x.Remove(0, idx);

				File.Move(x, newPath + chunkName);
				// trying to re-add compressed data to queue (copy and move situation)
				if (!x.EndsWith(ChunkFSConstants.CompressedFileExtension))
					CompressorQueue.Instance.AddFileToCompressQueueIfNotExists(newPath + chunkName, x);
			});
		}

		public static void DeleteFile(string fileName)
		{
			DeleteAllChunks(Path.GetDirectoryName(fileName), Path.GetFileName(fileName));
			File.Delete(fileName + ChunkFSConstants.MetaFileExtension);
		}

		public void SetLength(long length)
		{
			if (_length.HasValue && _length == length)
				return;
			var maxChunkNumber = Math.Max((length - 1) / ChunkFSConstants.ChunkSize, 0);
			var lastChunkSize = length - maxChunkNumber * ChunkFSConstants.ChunkSize;
			GetExistingFiles(_directory, _filePrefix)
				.Select(x => x.EndsWith(ChunkFSConstants.CompressedFileExtension) ? x.Substring(0, x.Length - ChunkFSConstants.CompressedFileExtension.Length) : x)
				.Where(x => Convert.ToInt32(Path.GetExtension(x).Remove(0, ChunkFSConstants.ChunkExtension.Length)) > maxChunkNumber)
				.ToList()
				.ForEach(x =>
				{
					File.Delete(x);
					CompressorQueue.Instance.DeleteCompressedFile(x);
				});

			for (var i = 0; i < maxChunkNumber; i++)
			{
				OpenWriteChunk(i);
				_currentWriteStream.SetLength(ChunkFSConstants.ChunkSize);
			}

			OpenWriteChunk(maxChunkNumber);
			_currentWriteStream.SetLength(lastChunkSize);
			_length = length;
		}

		public static FileSecurity GetAccessControl(string fileName)
		{
			return File.GetAccessControl(fileName + ChunkFSConstants.MetaFileExtension);
		}

		public static void SetAccessControl(string fileName, FileSecurity security)
		{
			// think about setting access rights to all files
			File.SetAccessControl(fileName + ChunkFSConstants.MetaFileExtension, security);
			// GetExistingFiles().ForEach(x => File.SetAccessControl(x, security));
		}

		public static bool Exists(string fileName)
		{
			return File.Exists(fileName + ChunkFSConstants.MetaFileExtension);
		}

		public static FileAttributes GetAttributes(string fileName)
		{
			return File.GetAttributes(fileName + ChunkFSConstants.MetaFileExtension);
		}

		public static void SetAttributes(string fileName, FileAttributes attributes)
		{
			Console.WriteLine("SetAttr: " + attributes);
			File.SetAttributes(fileName + ChunkFSConstants.MetaFileExtension, attributes);
		}

		private long? _length;

		public static long GetLength(string fileName)
		{
			var ef = GetExistingFiles(Path.GetDirectoryName(fileName) ?? string.Empty, Path.GetFileName(fileName));
			return ef.Sum(x => x.EndsWith(ChunkFSConstants.CompressedFileExtension)
				? ChunkFSConstants.ChunkSize
				: new FileInfo(x).Length);
		}

		public static FileInformation GetFileInfo(string fileName)
		{
			var finfo = new FileInfo(fileName + ChunkFSConstants.MetaFileExtension);

			var l = new FileInformation
			{
				Attributes = finfo.Attributes,
				CreationTime = finfo.CreationTime,
				LastAccessTime = finfo.LastAccessTime,
				FileName = finfo.Name.Remove(finfo.Name.Length - ChunkFSConstants.MetaFileExtension.Length),
				LastWriteTime = finfo.LastWriteTime,
				Length = GetLength(fileName)
			};
			// Console.WriteLine("GetFileInfo: " + _fileFullPath + " " + GetExistingFiles().Count);

			return l;
		}
	}
}