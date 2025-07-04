// (c) Copyright Ascensio System SIA 2009-2025
// 
// This program is a free software product.
// You can redistribute it and/or modify it under the terms
// of the GNU Affero General Public License (AGPL) version 3 as published by the Free Software
// Foundation. In accordance with Section 7(a) of the GNU AGPL its Section 15 shall be amended
// to the effect that Ascensio System SIA expressly excludes the warranty of non-infringement of
// any third-party rights.
// 
// This program is distributed WITHOUT ANY WARRANTY, without even the implied warranty
// of MERCHANTABILITY or FITNESS FOR A PARTICULAR  PURPOSE. For details, see
// the GNU AGPL at: http://www.gnu.org/licenses/agpl-3.0.html
// 
// You can contact Ascensio System SIA at Lubanas st. 125a-25, Riga, Latvia, EU, LV-1021.
// 
// The  interactive user interfaces in modified source and object code versions of the Program must
// display Appropriate Legal Notices, as required under Section 5 of the GNU AGPL version 3.
// 
// Pursuant to Section 7(b) of the License you must retain the original Product logo when
// distributing the program. Pursuant to Section 7(e) we decline to grant you any rights under
// trademark law for use of our trademarks.
// 
// All the Product's GUI elements, including illustrations and icon sets, as well as technical writing
// content are licensed under the terms of the Creative Commons Attribution-ShareAlike 4.0
// International. See the License terms at http://creativecommons.org/licenses/by-sa/4.0/legalcode

namespace ASC.Files.Thirdparty.ProviderDao;

[Scope(typeof(IFileDao<string>))]
internal class ProviderFileDao(
    IServiceProvider serviceProvider,
    TenantManager tenantManager,
    CrossDao crossDao,
    SelectorFactory selectorFactory,
    ISecurityDao<string> securityDao)
    : ProviderDaoBase(serviceProvider, tenantManager, crossDao, selectorFactory, securityDao), IFileDao<string>
{
    public async Task InvalidateCacheAsync(string fileId)
    {
        var selector = _selectorFactory.GetSelector(fileId);
        var fileDao = selector.GetFileDao(fileId);

        await fileDao.InvalidateCacheAsync(selector.ConvertId(fileId));
    }

    public async Task<File<string>> GetFileAsync(string fileId)
    {
        var selector = _selectorFactory.GetSelector(fileId);

        var fileDao = selector.GetFileDao(fileId);
        var result = await fileDao.GetFileAsync(selector.ConvertId(fileId));

        return result;
    }

    public async Task<File<string>> GetFileAsync(string fileId, int fileVersion)
    {
        var selector = _selectorFactory.GetSelector(fileId);

        var fileDao = selector.GetFileDao(fileId);
        var result = await fileDao.GetFileAsync(selector.ConvertId(fileId), fileVersion);

        return result;
    }

    public async Task<File<string>> GetFileAsync(string parentId, string title)
    {
        var selector = _selectorFactory.GetSelector(parentId);
        var fileDao = selector.GetFileDao(parentId);
        var result = await fileDao.GetFileAsync(selector.ConvertId(parentId), title);

        return result;
    }

    public async Task<File<string>> GetFileStableAsync(string fileId, int fileVersion = -1)
    {
        var selector = _selectorFactory.GetSelector(fileId);

        var fileDao = selector.GetFileDao(fileId);
        var result = await fileDao.GetFileAsync(selector.ConvertId(fileId), fileVersion);

        return result;
    }

    public IAsyncEnumerable<File<string>> GetFileHistoryAsync(string fileId)
    {
        var selector = _selectorFactory.GetSelector(fileId);
        var fileDao = selector.GetFileDao(fileId);

        return fileDao.GetFileHistoryAsync(selector.ConvertId(fileId));
    }

    public async IAsyncEnumerable<File<string>> GetFilesAsync(IEnumerable<string> fileIds)
    {
        foreach (var (selectorLocal, matchedIds) in _selectorFactory.GetSelectors(fileIds))
        {
            if (selectorLocal == null)
            {
                continue;
            }

            foreach (var matchedId in matchedIds.GroupBy(selectorLocal.GetIdCode))
            {
                var fileDao = selectorLocal.GetFileDao(matchedId.FirstOrDefault());

                await foreach (var file in fileDao.GetFilesAsync(matchedId.Select(selectorLocal.ConvertId).ToList()))
                {
                    if (file != null)
                    {
                        yield return file;
                    }
                }
            }
        }
    }

    public async IAsyncEnumerable<File<string>> GetFilesFilteredAsync(IEnumerable<string> fileIds, FilterType filterType, bool subjectGroup, Guid subjectID, string searchText, string[] extension, bool searchInContent, bool checkShared = false)
    {
        foreach (var (selectorLocal, matchedIds) in _selectorFactory.GetSelectors(fileIds))
        {
            if (selectorLocal == null)
            {
                continue;
            }

            foreach (var matchedId in matchedIds.GroupBy(selectorLocal.GetIdCode))
            {
                var fileDao = selectorLocal.GetFileDao(matchedId.FirstOrDefault());

                await foreach (var file in fileDao.GetFilesFilteredAsync(matchedId.Select(selectorLocal.ConvertId).ToArray(), filterType, subjectGroup, subjectID, searchText, 
                                   extension, searchInContent))
                {
                    if (file != null)
                    {
                        yield return file;
                    }
                }
            }
        }
    }

    public async IAsyncEnumerable<string> GetFilesAsync(string parentId)
    {
        var selector = _selectorFactory.GetSelector(parentId);
        var fileDao = selector.GetFileDao(parentId);
        var files = fileDao.GetFilesAsync(selector.ConvertId(parentId));

        await foreach (var f in files.Where(r => r != null))
        {
            yield return f;
        }
    }

    public async IAsyncEnumerable<File<string>> GetFilesAsync(string parentId, OrderBy orderBy, FilterType filterType, bool subjectGroup, Guid subjectID, string searchText,
        string[] extension, bool searchInContent, bool withSubfolders = false, bool excludeSubject = false, int offset = 0, int count = -1, string roomId = null, bool withShared = false, bool containingMyFiles = false, FolderType parentType = FolderType.DEFAULT, FormsItemDto formsItemDto = null, bool applyFormStepFilter = false)
    {
        var selector = _selectorFactory.GetSelector(parentId);

        var fileDao = selector.GetFileDao(parentId);
        var files = fileDao.GetFilesAsync(selector.ConvertId(parentId), orderBy, filterType, subjectGroup, subjectID, searchText, extension, searchInContent, withSubfolders, excludeSubject, formsItemDto: formsItemDto);
        var result = await files.Where(r => r != null).ToListAsync();

        foreach (var r in result)
        {
            yield return r;
        }
    }

    public override Task<Stream> GetFileStreamAsync(File<string> file)
    {
        return GetFileStreamAsync(file, 0);
    }

    /// <summary>
    /// Get stream of file
    /// </summary>
    /// <param name="file"></param>
    /// <param name="offset"></param>
    /// <returns>Stream</returns>
    public async Task<Stream> GetFileStreamAsync(File<string> file, long offset)
    {
        return await GetFileStreamAsync(file, offset, long.MaxValue);
    }
    
    public async Task<Stream> GetFileStreamAsync(File<string> file, long offset, long length)
    {
        ArgumentNullException.ThrowIfNull(file);

        var fileId = file.Id;
        var selector = _selectorFactory.GetSelector(fileId);
        file.Id = selector.ConvertId(fileId);

        var fileDao = selector.GetFileDao(fileId);
        var stream = await fileDao.GetFileStreamAsync(file, offset, length);
        file.Id = fileId; //Restore id

        return stream;
    }


    public async Task<long> GetFileSizeAsync(File<string> file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var fileId = file.Id;
        var selector = _selectorFactory.GetSelector(fileId);
        file.Id = selector.ConvertId(fileId);

        var fileDao = selector.GetFileDao(fileId);
        var size = await fileDao.GetFileSizeAsync(file);
        file.Id = fileId; //Restore id

        return size;
    }



    public async Task<bool> IsSupportedPreSignedUriAsync(File<string> file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var fileId = file.Id;
        var selector = _selectorFactory.GetSelector(fileId);
        file.Id = selector.ConvertId(fileId);

        var fileDao = selector.GetFileDao(fileId);
        var isSupported = await fileDao.IsSupportedPreSignedUriAsync(file);
        file.Id = fileId; //Restore id

        return isSupported;
    }

    public async Task<string> GetPreSignedUriAsync(File<string> file, TimeSpan expires, string shareKey = null)
    {
        ArgumentNullException.ThrowIfNull(file);

        var fileId = file.Id;
        var selector = _selectorFactory.GetSelector(fileId);
        file.Id = selector.ConvertId(fileId);

        var fileDao = selector.GetFileDao(fileId);
        var streamUri = await fileDao.GetPreSignedUriAsync(file, expires, shareKey);
        file.Id = fileId; //Restore id

        return streamUri;
    }
    public async Task<File<string>> SaveFileAsync(File<string> file, Stream fileStream, bool checkFolder)
    {
        return await SaveFileAsync(file, fileStream);
    }
    public async Task<File<string>> SaveFileAsync(File<string> file, Stream fileStream)
    {
        ArgumentNullException.ThrowIfNull(file);

        var fileId = file.Id;
        var folderId = file.ParentId;

        IDaoSelector selector;
        File<string> fileSaved = null;
        //Convert
        if (fileId != null)
        {
            selector = _selectorFactory.GetSelector(fileId);
            file.Id = selector.ConvertId(fileId);
            if (folderId != null)
            {
                file.ParentId = selector.ConvertId(folderId);
            }

            var fileDao = selector.GetFileDao(fileId);
            fileSaved = await fileDao.SaveFileAsync(file, fileStream);
        }
        else if (folderId != null)
        {
            selector = _selectorFactory.GetSelector(folderId);
            file.ParentId = selector.ConvertId(folderId);
            var fileDao = selector.GetFileDao(folderId);
            fileSaved = await fileDao.SaveFileAsync(file, fileStream);
        }

        if (fileSaved != null)
        {
            return fileSaved;
        }

        throw new ArgumentException("No file id or folder id toFolderId determine provider");
    }

    public async Task<File<string>> ReplaceFileVersionAsync(File<string> file, Stream fileStream)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (file.Id == null)
        {
            throw new ArgumentException("No file id or folder id toFolderId determine provider");
        }

        var fileId = file.Id;
        var folderId = file.ParentId;

        //Convert
        var selector = _selectorFactory.GetSelector(fileId);

        file.Id = selector.ConvertId(fileId);
        if (folderId != null)
        {
            file.ParentId = selector.ConvertId(folderId);
        }

        var fileDao = selector.GetFileDao(fileId);

        return await fileDao.ReplaceFileVersionAsync(file, fileStream);
    }

    public async Task DeleteFileAsync(string fileId)
    {
        await DeleteFileAsync(fileId, Guid.Empty);
    }

    public async Task DeleteFileVersionAsync(File<string> file, int version)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (file.Id == null)
        {
            throw new ArgumentException("No file id or folder id toFolderId determine provider");
        }

        var fileId = file.Id;
        var folderId = file.ParentId;

        //Convert
        var selector = _selectorFactory.GetSelector(fileId);

        file.Id = selector.ConvertId(fileId);
        if (folderId != null)
        {
            file.ParentId = selector.ConvertId(folderId);
        }

        var fileDao = selector.GetFileDao(fileId);

        await fileDao.DeleteFileVersionAsync(file, version);
    }

    public async Task DeleteFileAsync(string fileId, Guid ownerId)
    {
        var selector = _selectorFactory.GetSelector(fileId);
        var fileDao = selector.GetFileDao(fileId);

        await fileDao.DeleteFileAsync(selector.ConvertId(fileId), ownerId);
    }

    public async Task<bool> IsExistAsync(string title, string folderId)
    {
        var selector = _selectorFactory.GetSelector(folderId);

        var fileDao = selector.GetFileDao(folderId);

        return await fileDao.IsExistAsync(title, selector.ConvertId(folderId));
    }

    public async Task<bool> IsExistAsync(string title, int category, string folderId)
    {
        return await IsExistAsync(title, folderId);
    }

    public async Task<TTo> MoveFileAsync<TTo>(string fileId, TTo toFolderId, bool deleteLinks = false)
    {
        if (toFolderId is int tId)
        {
            return IdConverter.Convert<TTo>(await MoveFileAsync(fileId, tId, deleteLinks));
        }

        if (toFolderId is string tsId)
        {
            return IdConverter.Convert<TTo>(await MoveFileAsync(fileId, tsId, deleteLinks));
        }

        throw new NotImplementedException();
    }

    public async Task<int> MoveFileAsync(string fileId, int toFolderId, bool deleteLinks = false)
    {
        var movedFile = await PerformCrossDaoFileCopyAsync(fileId, toFolderId, true);

        return movedFile.Id;
    }

    public async Task<string> MoveFileAsync(string fileId, string toFolderId, bool deleteLinks = false)
    {
        var selector = _selectorFactory.GetSelector(fileId);
        if (IsCrossDao(fileId, toFolderId))
        {
            var movedFile = await PerformCrossDaoFileCopyAsync(fileId, toFolderId, true);

            return movedFile.Id;
        }

        var fileDao = selector.GetFileDao(fileId);

        return await fileDao.MoveFileAsync(selector.ConvertId(fileId), selector.ConvertId(toFolderId), deleteLinks);
    }

    public async Task<File<TTo>> CopyFileAsync<TTo>(string fileId, TTo toFolderId)
    {
        if (toFolderId is int tId)
        {
            return await CopyFileAsync(fileId, tId) as File<TTo>;
        }

        if (toFolderId is string tsId)
        {
            return await CopyFileAsync(fileId, tsId) as File<TTo>;
        }

        throw new NotImplementedException();
    }

    public async Task<File<int>> CopyFileAsync(string fileId, int toFolderId)
    {
        return await PerformCrossDaoFileCopyAsync(fileId, toFolderId, false);
    }

    public async Task<File<string>> CopyFileAsync(string fileId, string toFolderId)
    {
        var selector = _selectorFactory.GetSelector(fileId);
        if (IsCrossDao(fileId, toFolderId))
        {
            return await PerformCrossDaoFileCopyAsync(fileId, toFolderId, false);
        }

        var fileDao = selector.GetFileDao(fileId);

        return await fileDao.CopyFileAsync(selector.ConvertId(fileId), selector.ConvertId(toFolderId));
    }

    public async Task<string> FileRenameAsync(File<string> file, string newTitle)
    {
        var selector = _selectorFactory.GetSelector(file.Id);
        var fileId = file.Id;
        var parentId = file.ParentId;
        
        var fileDao = selector.GetFileDao(file.Id);
        file.Id = ConvertId(file.Id);
        file.ParentId = ConvertId(file.ParentId);

        var newFileId = await fileDao.FileRenameAsync(file, newTitle);

        file.Id = fileId;
        file.ParentId = parentId;
        
        return newFileId;
    }

    public async Task<string> UpdateCommentAsync(string fileId, int fileVersion, string comment)
    {
        var selector = _selectorFactory.GetSelector(fileId);

        var fileDao = selector.GetFileDao(fileId);

        return await fileDao.UpdateCommentAsync(selector.ConvertId(fileId), fileVersion, comment);
    }

    public async Task CompleteVersionAsync(string fileId, int fileVersion)
    {
        var selector = _selectorFactory.GetSelector(fileId);

        var fileDao = selector.GetFileDao(fileId);

        await fileDao.CompleteVersionAsync(selector.ConvertId(fileId), fileVersion);
    }

    public async Task ContinueVersionAsync(string fileId, int fileVersion)
    {
        var selector = _selectorFactory.GetSelector(fileId);
        var fileDao = selector.GetFileDao(fileId);

        await fileDao.ContinueVersionAsync(selector.ConvertId(fileId), fileVersion);
    }

    public bool UseTrashForRemove(File<string> file)
    {
        var selector = _selectorFactory.GetSelector(file.Id);
        var fileDao = selector.GetFileDao(file.Id);

        return fileDao.UseTrashForRemove(file);
    }

    public Task SaveFormRoleMapping(string formId, IEnumerable<FormRole> formRoles)
    {
        return Task.CompletedTask;
    }
    public IAsyncEnumerable<FormRole> GetFormRoles(string formId)
    {
        return AsyncEnumerable.Empty<FormRole>();
    }
    public Task<(int, List<FormRole>)> GetUserFormRoles(string formId, Guid userId)
    {
        return Task.FromResult((-1, new List<FormRole>()));
    }
    public IAsyncEnumerable<FormRole> GetUserFormRolesInRoom(string roomId, Guid userId)
    {
        return AsyncEnumerable.Empty<FormRole>();
    }
    public Task<FormRole> ChangeUserFormRoleAsync(string formId, FormRole formRole)
    {
        return Task.FromResult<FormRole>(null);
    }
    public Task DeleteFormRolesAsync(string formId)
    {
        return Task.CompletedTask;
    }

    #region chunking

    public async Task<ChunkedUploadSession<string>> CreateUploadSessionAsync(File<string> file, long contentLength)
    {
        var fileDao = GetFileDao(file);

        return await fileDao.CreateUploadSessionAsync(ConvertId(file), contentLength);
    }

    public async Task<File<string>> UploadChunkAsync(ChunkedUploadSession<string> uploadSession, Stream chunkStream, long chunkLength, int? chunkNumber = null)
    {
        if (chunkNumber.HasValue)
        {
            throw new ArgumentException("Can not async upload in provider folder.");
        }
        var fileDao = GetFileDao(uploadSession.File);
        uploadSession.File = ConvertId(uploadSession.File);
        await fileDao.UploadChunkAsync(uploadSession, chunkStream, chunkLength);

        return uploadSession.File;
    }

    public async Task<File<string>> FinalizeUploadSessionAsync(ChunkedUploadSession<string> uploadSession)
    {
        var fileDao = GetFileDao(uploadSession.File);
        uploadSession.File = ConvertId(uploadSession.File);
        return await fileDao.FinalizeUploadSessionAsync(uploadSession);
    }

    public async Task AbortUploadSessionAsync(ChunkedUploadSession<string> uploadSession)
    {
        var fileDao = GetFileDao(uploadSession.File);
        uploadSession.File = ConvertId(uploadSession.File);

        await fileDao.AbortUploadSessionAsync(uploadSession);
    }

    private IFileDao<string> GetFileDao(File<string> file)
    {
        if (file.Id != null)
        {
            return _selectorFactory.GetSelector(file.Id).GetFileDao(file.Id);
        }

        if (file.ParentId != null)
        {
            return _selectorFactory.GetSelector(file.ParentId).GetFileDao(file.ParentId);
        }

        throw new ArgumentException("Can't create instance of dao for given file.", nameof(file));
    }

    private string ConvertId(string id)
    {
        return id != null ? _selectorFactory.GetSelector(id).ConvertId(id) : null;
    }

    private File<string> ConvertId(File<string> file)
    {
        file.Id = ConvertId(file.Id);
        file.ParentId = ConvertId(file.ParentId);

        return file;
    }

    public override Task<Stream> GetThumbnailAsync(string fileId, uint width, uint height)
    {
        var selector = _selectorFactory.GetSelector(fileId);
        var fileDao = selector.GetFileDao(fileId);
        return fileDao.GetThumbnailAsync(selector.ConvertId(fileId), width, height);
    }

    public override Task<Stream> GetThumbnailAsync(File<string> file, uint width, uint height)
    {
        var fileDao = GetFileDao(file);
        return fileDao.GetThumbnailAsync(file, width, height);
    }

    public async Task<int> SetCustomOrder(string fileId, string parentFolderId, int order)
    {
        var selector = _selectorFactory.GetSelector(fileId);
        var fileDao = selector.GetFileDao(fileId);
        return await fileDao.SetCustomOrder(fileId, parentFolderId, order);
    }

    public async Task InitCustomOrder(Dictionary<string, int> fileIds, string parentFolderId)
    {
        var selector = _selectorFactory.GetSelector(parentFolderId);
        var fileDao = selector.GetFileDao(parentFolderId);
        await fileDao.InitCustomOrder(fileIds, parentFolderId);
    }

    public Task<long> GetTransferredBytesCountAsync(ChunkedUploadSession<string> uploadSession)
    {
        var fileDao = GetFileDao(uploadSession.File);
        uploadSession.File = ConvertId(uploadSession.File);
        return fileDao.GetTransferredBytesCountAsync(uploadSession);
    }

    #endregion
}
