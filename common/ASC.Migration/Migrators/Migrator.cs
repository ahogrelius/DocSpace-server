﻿// (c) Copyright Ascensio System SIA 2009-2025
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

namespace ASC.Migration.Core.Migrators;

public abstract class Migrator(
    SecurityContext securityContext,
    UserManager userManager,
    TenantQuotaFeatureStatHelper tenantQuotaFeatureStatHelper,
    QuotaSocketManager quotaSocketManager,
    FileStorageService fileStorageService,
    GlobalFolderHelper globalFolderHelper,
    IServiceProvider serviceProvider,
    IDaoFactory daoFactory,
    EntryManager entryManager,
    MigrationLogger migrationLogger,
    AuthContext authContext,
    DisplayUserSettingsHelper displayUserSettingsHelper,
    UserManagerWrapper userManagerWrapper,
    UserSocketManager socketManager)
    : IAsyncDisposable
{
    protected SecurityContext SecurityContext { get; } = securityContext;
    protected UserSocketManager SocketManager { get; } = socketManager;
    protected UserManager UserManager { get; } = userManager;
    private TenantQuotaFeatureStatHelper TenantQuotaFeatureStatHelper { get; } = tenantQuotaFeatureStatHelper;
    private QuotaSocketManager QuotaSocketManager { get; } = quotaSocketManager;
    private FileStorageService FileStorageService { get; } = fileStorageService;
    private GlobalFolderHelper GlobalFolderHelper { get; } = globalFolderHelper;
    private IServiceProvider ServiceProvider { get; } = serviceProvider;
    private IDaoFactory DaoFactory { get; } = daoFactory;
    private EntryManager EntryManager { get; } = entryManager;
    protected MigrationLogger MigrationLogger { get; } = migrationLogger;
    private AuthContext AuthContext { get; } = authContext;
    protected DisplayUserSettingsHelper DisplayUserSettingsHelper { get; } = displayUserSettingsHelper;
    private UserManagerWrapper UserManagerWrapper { get; } = userManagerWrapper;

    public MigrationInfo MigrationInfo { get; protected init; }
    private IAccount _currentUser;
    private Dictionary<string, MigrationUser> _usersForImport;
    private List<string> _importedUsers;
    private List<string> _failedUsers;
    private const string FolderKey = "folder";
    private const string FileKey = "file";

    protected double _lastProgressUpdate;
    protected string _lastStatusUpdate;

    protected string TmpFolder { get; set; }

    public Func<double, string, Task> OnProgressUpdateAsync { get; set; }

    public abstract Task InitAsync(string path, OperationType operation, CancellationToken cancellationToken);
    public abstract Task<MigrationApiInfo> ParseAsync(bool reportProgress = true);

    protected async Task ReportProgressAsync(double value, string status)
    {
        _lastProgressUpdate = value;
        _lastStatusUpdate = status;
        if (OnProgressUpdateAsync != null)
        {
            await OnProgressUpdateAsync(value, status);
        }
        MigrationLogger.Log($"{value:0.00} progress: {status}");
    }

    public void Log(string msg, Exception exception = null)
    {
        MigrationLogger.Log(msg, exception);
    }

    public string GetLogName()
    {
        return MigrationLogger.GetLogName();
    }

    public List<string> GetGuidImportedUsers()
    {
        return _importedUsers;
    }

    public async Task MigrateAsync(MigrationApiInfo migrationInfo)
    {
        await ReportProgressAsync(0, MigrationResource.PreparingForMigration);
        _currentUser = AuthContext.CurrentAccount;
        _importedUsers = [];
        _failedUsers = [];

        MigrationInfo.Merge(migrationInfo);

        _usersForImport = MigrationInfo.Users.Where(u => u.Value.ShouldImport).ToDictionary();

        await MigrateUsersAsync();

        await MigrateGroupAsync();

        var progressStep = _usersForImport.Count == 0 ? 30 : 30 / _usersForImport.Count;
        var i = 1;
        foreach (var kv in _usersForImport.Where(u=> !_failedUsers.Contains(u.Value.Info.Email)))
        {
            try
            {
                await ReportProgressAsync(_lastProgressUpdate + progressStep, string.Format(MigrationResource.MigratingUserFiles, kv.Value.Info.DisplayUserName(DisplayUserSettingsHelper), i++, _usersForImport.Count));
                await MigrateStorageAsync(kv.Value.Storage, kv.Value);
            }
            catch(Exception e)
            {
                Log(MigrationResource.CanNotImportUserFiles, e);
                MigrationInfo.Errors.Add($"{kv.Key} - {MigrationResource.CanNotImportUserFiles}"); 
            }
        }

        if(MigrationInfo.CommonStorage != null)
        {
            try
            {
                await ReportProgressAsync(85, string.Format(MigrationResource.MigrationCommonFiles));
                await MigrateStorageAsync(MigrationInfo.CommonStorage);
            }
            catch(Exception e)
            {
                Log(MigrationResource.СanNotImportCommonFiles, e);
                MigrationInfo.Errors.Add(MigrationResource.СanNotImportCommonFiles);
            }
        }

        if (MigrationInfo.ProjectStorage != null)
        {
            try
            {
                await ReportProgressAsync(90, string.Format(MigrationResource.MigrationProjectFiles));
                await MigrateStorageAsync(MigrationInfo.ProjectStorage);
            }
            catch (Exception e)
            {
                Log(MigrationResource.СanNotImportProjectFiles, e);
                MigrationInfo.Errors.Add(MigrationResource.СanNotImportProjectFiles);
            }
        }

        if (Directory.Exists(TmpFolder))
        {
            Directory.Delete(TmpFolder, true);
        }

        MigrationInfo.FailedUsers = _failedUsers.Count;
        MigrationInfo.SuccessedUsers = _usersForImport.Count - MigrationInfo.FailedUsers;
        await ReportProgressAsync(100, MigrationResource.MigrationCompleted);
    }

    private async Task MigrateUsersAsync()
    {
        var i = 1;
        var progressStep = _usersForImport.Count == 0 ? 30 : 30 / _usersForImport.Count;
        foreach (var (key, user) in MigrationInfo.Users)
        {
            try
            {
                if (user.ShouldImport)
                {
                    await ReportProgressAsync(_lastProgressUpdate + progressStep, string.Format(MigrationResource.UserMigration, user.Info.DisplayUserName(DisplayUserSettingsHelper), i++, MigrationInfo.Users.Count));
                }
                var saved = await UserManager.GetUserByEmailAsync(user.Info.Email);

                if (user.ShouldImport && (saved.Equals(Constants.LostUser) || saved.Removed))
                {
                    DataСhange(user);
                    user.Info.UserName = await UserManagerWrapper.MakeUniqueNameAsync(user.Info);
                    user.Info.ActivationStatus = EmployeeActivationStatus.Pending;
                    saved = await UserManager.SaveUserInfo(user.Info, user.UserType);
                    await SocketManager.AddUserAsync(saved);
                    var groupId = user.UserType switch
                    {
                        EmployeeType.User => Constants.GroupUser.ID,
                        EmployeeType.DocSpaceAdmin => Constants.GroupAdmin.ID,
                        EmployeeType.RoomAdmin => Constants.GroupRoomAdmin.ID,
                        _ => Guid.Empty
                    };

                    if (groupId != Guid.Empty)
                    {
                        await UserManager.AddUserIntoGroupAsync(saved.Id, groupId, true);
                    }
                    else if (user.UserType == EmployeeType.RoomAdmin)
                    {
                        var (name, value) = await TenantQuotaFeatureStatHelper.GetStatAsync<CountPaidUserFeature, int>();
                        _ = QuotaSocketManager.ChangeQuotaUsedValueAsync(name, value);
                    }

                    if (user.HasPhoto)
                    {
                        using var ms = new MemoryStream();
                        await using (var fs = File.OpenRead(user.PathToPhoto))
                        {
                            await fs.CopyToAsync(ms);
                        }
                        await UserManager.SaveUserPhotoAsync(user.Info.Id, ms.ToArray());
                    }
                }
                if (saved.Equals(Constants.LostUser))
                {
                    MigrationInfo.Users.Remove(key);
                }
                else
                {
                    user.Info = saved;
                }

                if (user.ShouldImport)
                {
                    _importedUsers.Add(user.Info.Email);
                }
            }
            catch(Exception e)
            {
                Log(MigrationResource.CanNotImportUser, e);
                MigrationInfo.Errors.Add($"{key} - {MigrationResource.CanNotImportUser}");
                _failedUsers.Add(user.Info.Email);
                MigrationInfo.Users.Remove(key);
            }
        }
    }

    private static void DataСhange(MigrationUser user)
    {
        user.Info.UserName ??= user.Info.Email.Split('@').First();
        user.Info.LastName ??= user.Info.FirstName;
        }

    private async Task MigrateGroupAsync()
    {
        var i = 1;
        var progressStep = MigrationInfo.Groups.Count == 0 ? 20 : 20 / MigrationInfo.Groups.Count;
        foreach (var kv in MigrationInfo.Groups)
        {
            var group = kv.Value;

            await ReportProgressAsync(_lastProgressUpdate + progressStep, string.Format(MigrationResource.GroupMigration, group.Info.Name, i++, MigrationInfo.Groups.Count));
            
            if (!group.ShouldImport)
            {
                return;
            }
            var existingGroups = (await UserManager.GetGroupsAsync()).ToList();
            var oldGroup = existingGroups.Find(g => g.Name == group.Info.Name);
            if (oldGroup != null)
            {
                group.Info = oldGroup;
            }
            else
            {
                group.Info = await UserManager.SaveGroupInfoAsync(group.Info);
            }

            foreach (var userGuid in group.UserKeys)
            {
                try
                {
                    var user = _usersForImport.TryGetValue(userGuid, out var value) ? value.Info : Constants.LostUser;
                    if (user.Equals(Constants.LostUser))
                    {
                        continue;
                    }
                    if (!await UserManager.IsUserInGroupAsync(user.Id, group.Info.ID))
                    {
                        await UserManager.AddUserIntoGroupAsync(user.Id, group.Info.ID);
                        if (group.ManagerKey == userGuid)
                        {
                            await UserManager.SetDepartmentManagerAsync(group.Info.ID, user.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log(string.Format(MigrationResource.CanNotAddUserInGroup, userGuid, group.Info.Name), ex);
                    MigrationInfo.Errors.Add(string.Format(MigrationResource.CanNotAddUserInGroup, userGuid, group.Info.Name));
                }
            }
        }
    }

    private async Task MigrateStorageAsync(MigrationStorage storage, MigrationUser user = null)
    {
        if (!storage.ShouldImport || storage.Files.Count == 0 && storage.Folders.Count == 0)
        {
            return;
        }

        if (user != null)
        {
            await SecurityContext.AuthenticateMeAsync(user.Info.Id);
        }
        else
        {
            await SecurityContext.AuthenticateMeAsync(_currentUser);
        }

        var matchingFilesIds = new Dictionary<string, FileEntry<int>>();
        Folder<int> newFolder;
        if (storage.Type != FolderType.BUNCH) 
        {
            newFolder = storage.Type == FolderType.USER
            ? await FileStorageService.CreateFolderAsync(await GlobalFolderHelper.FolderMyAsync, $"ASC migration files {DateTime.Now:dd.MM.yyyy}")
                    : await FileStorageService.CreateRoomAsync($"ASC migration common files {DateTime.Now:dd.MM.yyyy}", RoomType.PublicRoom, false, false, new List<FileShareParams>(), 0, null, false, null, null, null, null, null);
        Log(MigrationResource.СreateRootFolder);
        }
        else
        {
            newFolder = ServiceProvider.GetService<Folder<int>>();
            newFolder.Id = -1;
        }

        matchingFilesIds.Add($"{FolderKey}-{storage.RootKey}", newFolder);
        var orderedFolders = storage.Folders.OrderBy(f => f.Level);
        foreach (var folder in orderedFolders)
        {
            if (!storage.ShouldImportSharedFolders ||
                !storage.Securities.Any(s => s.EntryId == folder.Id && s.EntryType == 1) && matchingFilesIds[$"{FolderKey}-{folder.ParentId}"].Id != 0)
            {
                if (storage.Type == FolderType.BUNCH && !folder.Private)
                {
                    newFolder = await FileStorageService.CreateRoomAsync(folder.Title, RoomType.PublicRoom, false, false, new List<FileShareParams>(), 0, null, false, null, null, null, null, null);
                }
                else
                {
                    newFolder = await FileStorageService.CreateFolderAsync(matchingFilesIds[$"{FolderKey}-{folder.ParentId}"].Id, folder.Title);
                }

                Log(string.Format(MigrationResource.CreateFolder, newFolder.Title));
            }
            else
            {
                newFolder = ServiceProvider.GetService<Folder<int>>();
                newFolder.Title = folder.Title;
            }
            matchingFilesIds.Add($"{FolderKey}-{folder.Id}", newFolder);
        }

        var fileDao = DaoFactory.GetFileDao<int>();

        foreach (var file in storage.Files)
        {
            try
            {
                await using var fs = new FileStream(file.Path, FileMode.Open);

                var newFile = ServiceProvider.GetService<File<int>>();
                newFile.ParentId = matchingFilesIds[$"{FolderKey}-{file.Folder}"].Id;
                newFile.Comment = FilesCommonResource.CommentCreate;
                newFile.Title = Path.GetFileName(file.Title);
                newFile.ContentLength = fs.Length;
                newFile.Version = file.Version;
                newFile.VersionGroup = file.VersionGroup;
                newFile.Comment = file.Comment;
                newFile.CreateOn = file.Created;
                newFile.ModifiedOn = file.Modified;
                if (matchingFilesIds.ContainsKey($"{FileKey}-{file.Id}"))
                {
                    newFile.Id = matchingFilesIds[$"{FileKey}-{file.Id}"].Id;
                }
                if (!storage.ShouldImportSharedFolders || !storage.Securities.Any(s => s.EntryId == file.Folder && s.EntryType == 1) && newFile.ParentId != 0)
                {
                    newFile = await fileDao.SaveFileAsync(newFile, fs);
                    Log(string.Format(MigrationResource.CreateFile, file.Title));
                }
                if (!matchingFilesIds.ContainsKey($"{FileKey}-{file.Id}") && newFile.Id != 0)
                {
                    matchingFilesIds.Add($"{FileKey}-{file.Id}", newFile);
                }
            }
            catch (Exception ex)
            {
                Log(string.Format(MigrationResource.CanNotCreateFile, Path.GetFileName(file.Title)), ex);
                MigrationInfo.Errors.Add(string.Format(MigrationResource.CanNotCreateFile, Path.GetFileName(file.Title)));
            }
        }

        if (storage.Type == FolderType.COMMON || !storage.ShouldImportSharedFiles && !storage.ShouldImportSharedFolders)
        {
            return;
        }

        var aces = new Dictionary<string, AceWrapper>();
        var matchingRoomIds = new Dictionary<int, FileEntry<int>>();
        var innerFolders = new List<int>();
        var orderedSecurity = storage.Securities.OrderBy(s => OrderSecurity(storage,s));
        foreach (var security in orderedSecurity)
        {
            try
            {
                if (!MigrationInfo.Users.ContainsKey(security.Subject) && !MigrationInfo.Groups.ContainsKey(security.Subject))
                {
                    continue;
                }
                var access = (Files.Core.Security.FileShare)security.Security;

                    var entryIsFile = security.EntryType == 2;
                if (entryIsFile && storage.ShouldImportSharedFiles)
                {
                    var key = $"{FileKey}-{security.EntryId}";
                    if(!matchingFilesIds.ContainsKey(key))
                    {
                        continue;
                    }
                    await SecurityContext.AuthenticateMeAsync(user.Info.Id);
                    AceWrapper ace;
                    if (!aces.ContainsKey($"{security.Security}{matchingFilesIds[key].Id}"))
                    {
                        try
                        {
                            ace = await FileStorageService.SetExternalLinkAsync(matchingFilesIds[key].Id, FileEntryType.File, Guid.Empty, null, access, requiredAuth: true,
                                primary: false);
                            aces.Add($"{security.Security}{matchingFilesIds[key].Id}", ace);
                        }
                        catch
                        {
                            ace = null;
                            aces.Add($"{security.Security}{matchingFilesIds[key].Id}", null);
                        }
                    }
                    else
                    {
                        ace = aces[$"{security.Security}{matchingFilesIds[key].Id}"];
                    }
                    if (ace != null)
                    {
                        if (MigrationInfo.Users.TryGetValue(security.Subject, out var infoUser))
                        {
                            var userForShare = await UserManager.GetUsersAsync(infoUser.Info.Id);
                            await SecurityContext.AuthenticateMeAsync(userForShare.Id);
                            await EntryManager.MarkFileAsRecentByLink(matchingFilesIds[key] as File<int>, ace.Id);
                        }
                        else
                        {
                            var filter = new UserQueryFilter
                            {
                                EmployeeStatus = EmployeeStatus.Active,
                                IncludeGroups = [[MigrationInfo.Groups[security.Subject].Info.ID]],
                                SortType = UserSortType.FirstName,
                                SortOrderAsc = true,
                                IncludeStrangers = true,
                                Limit = 100000,
                                Offset = 0,
                                Area = Area.All
                            };
                            
                            var users = UserManager.GetUsers(filter).Where(u => u.Id != user.Info.Id);
                            await foreach (var u in users)
                            {
                                await SecurityContext.AuthenticateMeAsync(u.Id);
                                await EntryManager.MarkFileAsRecentByLink(matchingFilesIds[key] as File<int>, ace.Id);
                            }
                        }
                    }
                }
                else if (storage.ShouldImportSharedFolders)
                {
                    var localMatchingRoomIds = new Dictionary<int, FileEntry<int>>();
                    var key = $"{FolderKey}-{security.EntryId}";

                    if (innerFolders.Contains(security.EntryId))
                    {
                        continue;
                    }
                    if (!matchingRoomIds.ContainsKey(security.EntryId))
                    {
                        if (storage.Type == FolderType.BUNCH)
                        {
                            var owner = storage.Folders.FirstOrDefault(f => f.Id == security.EntryId).Owner;
                            user = MigrationInfo.Users[owner];
                        }

                        if (user.UserType == EmployeeType.User)
                        {
                            await SecurityContext.AuthenticateMeAsync(_currentUser);
                        }
                        else
                        {
                            await SecurityContext.AuthenticateMeAsync(user.Info.Id);
                        }
                        var room = await FileStorageService.CreateRoomAsync($"{matchingFilesIds[key].Title}", RoomType.EditingRoom, false, false, new List<FileShareParams>(), 0, null, false, null, null, null, null, null);

                        orderedFolders = storage.Folders.Where(f => f.ParentId == security.EntryId).OrderBy(f => f.Level);
                        matchingRoomIds.Add(security.EntryId, room);
                        localMatchingRoomIds.Add(security.EntryId, room);
                        Log(string.Format(MigrationResource.CreateShareRoom, room.Title));

                        if (user.UserType == EmployeeType.User)
                        {
                            var aceList = new List<AceWrapper>
                            {
                                new()
                                {
                                    Access = Files.Core.Security.FileShare.ContentCreator,
                                    Id = user.Info.Id
                                }
                            };

                            var collection = new AceCollection<int>
                            {
                                Files = [],
                                Folders = new List<int> { matchingRoomIds[security.EntryId].Id },
                                Aces = aceList,
                                Message = null
                            };

                            await FileStorageService.SetAceObjectAsync(collection, false);
                        }

                        foreach (var folder in orderedFolders)
                        {
                            newFolder = await FileStorageService.CreateFolderAsync(matchingRoomIds[folder.ParentId].Id, folder.Title);
                            matchingRoomIds.Add(folder.Id, newFolder);
                            innerFolders.Add(folder.Id);
                            Log(string.Format(MigrationResource.CreateFolder, newFolder.Title));
                        }
                        foreach (var file in storage.Files.Where(f => localMatchingRoomIds.ContainsKey(f.Folder)))
                        {
                            try
                            {
                                await using var fs = new FileStream(file.Path, FileMode.Open);

                                var newFile = ServiceProvider.GetService<File<int>>();
                                newFile.ParentId = localMatchingRoomIds[security.EntryId].Id;
                                newFile.Comment = FilesCommonResource.CommentCreate;
                                newFile.Title = Path.GetFileName(file.Title);
                                newFile.ContentLength = fs.Length;
                                newFile.Version = file.Version;
                                newFile.VersionGroup = file.VersionGroup;
                                newFile.CreateOn = file.Created;
                                newFile.ModifiedOn = file.Modified;
                                if (matchingFilesIds.ContainsKey($"{FileKey}-{file.Id}"))
                                {
                                    newFile.Id = matchingFilesIds[$"{FileKey}-{file.Id}"].Id;
                                }
                                newFile = await fileDao.SaveFileAsync(newFile, fs);
                                Log(string.Format(MigrationResource.CreateFile, file.Title));
                                if (!matchingFilesIds.ContainsKey($"{FileKey}-{file.Id}"))
                                {
                                    matchingFilesIds.Add($"{FileKey}-{file.Id}", newFile);
                                }
                            }
                            catch(Exception ex)
                            {
                                Log(string.Format(MigrationResource.CanNotCreateFile, Path.GetFileName(file.Title)), ex);
                                MigrationInfo.Errors.Add(string.Format(MigrationResource.CanNotCreateFile, Path.GetFileName(file.Title)));
                            }
                        }
                    }
                    if (_usersForImport.ContainsKey(security.Subject) && _currentUser.ID == _usersForImport[security.Subject].Info.Id)
                    {
                        continue;
                    }

                    var list = new List<AceWrapper>
                    {
                        new()
                        {
                            Access = access,
                            Id = MigrationInfo.Users.TryGetValue(security.Subject, out var infoUser) 
                                ? infoUser.Info.Id 
                                : MigrationInfo.Groups[security.Subject].Info.ID
                        }
                    };

                    var aceCollection = new AceCollection<int>
                    {
                        Files = [],
                        Folders = new List<int> { matchingRoomIds[security.EntryId].Id },
                        Aces = list,
                        Message = null
                    };

                    await FileStorageService.SetAceObjectAsync(aceCollection, false);
                }
            }
            catch (Exception ex)
            {
                Log(string.Format(MigrationResource.CanNotShare, security.EntryId, security.Subject), ex);
                MigrationInfo.Errors.Add(string.Format(MigrationResource.CanNotShare, security.EntryId, security.Subject));
            }
        }
    }

    private static int OrderSecurity(MigrationStorage storage, MigrationSecurity security)
    {
        if(security.EntryType != 1)
        {
            return 0;
        }
        var folder = storage.Folders.FirstOrDefault(f => f.Id == security.EntryId);
        return folder?.Level ?? 0;
    }

    public async ValueTask DisposeAsync()
    {
        if (MigrationLogger != null)
        {
            await MigrationLogger.DisposeAsync();
        }
    }
}
