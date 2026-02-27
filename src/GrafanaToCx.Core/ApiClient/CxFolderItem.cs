namespace GrafanaToCx.Core.ApiClient;

public sealed record CxFolderItem(string Id, string Name, string? ParentId = null);
