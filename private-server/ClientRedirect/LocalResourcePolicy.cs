namespace OverField.PrivateServer;

public static class LocalResourcePolicy
{
    // Values confirmed from this client's YooAsset EOperationStatus/EPlayMode metadata.
    public static bool CanContinue(bool resourceInitialized, int initializationStatus, int playMode, string? manifestVersion)
        => resourceInitialized && initializationStatus == 2 && playMode == 2 && !string.IsNullOrWhiteSpace(manifestVersion);

    public static bool HasAllResources(int missingDownloadCount) => missingDownloadCount == 0;
}
