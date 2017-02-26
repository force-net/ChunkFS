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

		private FileAccess _access;

		public FileAttributes? Attributes { get; set; }

		private FileStream _currentStream;

		private long _currentIndex = -1;

		public ChunkedFileInfo(string fileName)
			: this(fileName, FileAccess.Read, FileMode.Open)
		{
		}

		public ChunkedFileInfo(string fileName, FileAccess access, FileMode mode)
		{
			_filePrefix = Path.GetFileName(fileName);
			_directory = Path.GetDirectoryName(fileName);
			// meta file name with chunk suffix for better search and to ensure used won't try to open empty file by extension
			_fileFullPath = fileName + ChunkFSConstants.MetaFileExtension;
			_access = access;
			if (mode == FileMode.Truncate || mode == FileMode.Create)
				DeleteAllChunks();
			// Console.WriteLine(fileName + " " + mode);
			if (mode == FileMode.Create || mode == FileMode.OpenOrCreate || mode == FileMode.Append || mode == FileMode.CreateNew)
				CreateRealFile();
		}

		private void CreateRealFile()
		{
			if (!File.Exists(_fileFullPath))
				File.Create(_fileFullPath).Close();
		}


		public DateTime? CreationTime { get; set; }
		public DateTime? LastAccessTime { get; set; }
		public DateTime? LastWriteTime { get; set; }

		public void DeleteAllChunks()
		{
			GetExistingFiles().ForEach(File.Delete);
		}

		private List<string> GetExistingFiles()
		{
			// does not return meta chunk
			return Directory.GetFiles(_directory, _filePrefix + ChunkFSConstants.ChunkExtension + "*").Where(x => !x.EndsWith(ChunkFSConstants.MetaFileExtension)).ToList();
		}

		public void Write(byte[] buffer, long position)
		{
			var offset = 0;
			var count = buffer.Length;
			while (count > 0)
			{
				var requiredChunk = position / ChunkFSConstants.ChunkSize;
				OpenChunk(requiredChunk, false);
				var chunkOffset = (int) (position % ChunkFSConstants.ChunkSize);
				_currentStream.Position = chunkOffset;

				var toWrite = Math.Min(Math.Min(ChunkFSConstants.ChunkSize, count), ChunkFSConstants.ChunkSize - chunkOffset);
				_currentStream.Write(buffer, offset, toWrite);
				offset += toWrite;
				count -= toWrite;
				position += toWrite;
			}

			if (_length.HasValue && position > _length.Value)
				_length = position;
		}

		public int Read(byte[] buffer, long position)
		{
			var offset = 0;
			var count = buffer.Length;
			while (offset < buffer.Length)
			{
				var requiredChunk = position / ChunkFSConstants.ChunkSize;
				OpenChunk(requiredChunk, true);
				var chunkOffset = position % ChunkFSConstants.ChunkSize;
				_currentStream.Position = chunkOffset;

				var toRead = (int)Math.Min(Math.Min(ChunkFSConstants.ChunkSize, count), ChunkFSConstants.ChunkSize - chunkOffset);
				var readed = _currentStream.Read(buffer, offset, toRead);
				if (readed == 0)
					break;
				offset += readed;
				count -= readed;
				position += readed;
			}

			return offset;
		}

		private void OpenChunk(long requiredChunk, bool isRead)
		{
			if (_currentIndex != requiredChunk || _currentStream == null)
			{
				// will set data after our real close
				if (!isRead)
					_currentStream?.Flush();
				_currentStream?.Dispose();
				var path = Path.Combine(_directory, _filePrefix + ChunkFSConstants.ChunkExtension + requiredChunk.ToString("0000", CultureInfo.InvariantCulture));
				_currentStream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
				_currentIndex = requiredChunk;
			}
		}

		public void Close()
		{
			_currentStream?.Dispose();
			_currentStream = null;

			if (!File.Exists(_fileFullPath))
				return;

			if (Attributes != null)
				File.SetAttributes(_fileFullPath, Attributes.Value);
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
			_currentStream?.Flush();
		}

		public void MoveFile(string newpath)
		{
			File.Move(_fileFullPath, newpath);
			GetExistingFiles().ForEach(x =>
			{
				var chunkName = Path.GetExtension(x);
				File.Move(x, newpath + chunkName);
			});
		}

		public void DeleteFile()
		{
			DeleteAllChunks();
			File.Delete(_fileFullPath);
		}

		public void SetLength(long length)
		{
			var maxChunkNumber = Math.Max((length - 1) / ChunkFSConstants.ChunkSize, 0);
			var lastChunkSize = length - maxChunkNumber * ChunkFSConstants.ChunkSize;
			GetExistingFiles()
				.Where(x => Convert.ToInt32((Path.GetExtension(x) ?? string.Empty).Remove(0, ChunkFSConstants.ChunkExtension.Length)) > maxChunkNumber)
				.ToList()
				.ForEach(File.Delete);

			for (var i = 0; i < maxChunkNumber; i++)
			{
				OpenChunk(i, false);
				_currentStream.SetLength(ChunkFSConstants.ChunkSize);
			}

			OpenChunk(maxChunkNumber, false);
			_currentStream.SetLength(lastChunkSize);
		}

		public FileSecurity GetAccessControl()
		{
			return File.GetAccessControl(_fileFullPath);
		}

		public void SetAccessControl(FileSecurity security)
		{
			// think about setting access rights to all files
			File.SetAccessControl(_fileFullPath, security);
			// GetExistingFiles().ForEach(x => File.SetAccessControl(x, security));
		}

		public bool Exists()
		{
			return File.Exists(_fileFullPath);
		}

		public FileAttributes GetAttributes()
		{
			return Attributes ?? File.GetAttributes(_fileFullPath);
		}

		private long? _length;

		public FileInformation GetFileInfo()
		{
			var finfo = new FileInfo(_fileFullPath);
			return new FileInformation
			{
				Attributes = Attributes ?? finfo.Attributes,
				CreationTime = CreationTime ?? finfo.CreationTime,
				LastAccessTime = LastAccessTime ?? LastAccessTime,
				FileName = _filePrefix,
				LastWriteTime = LastWriteTime ?? LastWriteTime,
				Length = _length ?? (_length = GetExistingFiles().Sum(x => new FileInfo(x).Length)).Value
			};
		}

	}
}