using System;

namespace NotifyHub.Services;

public interface ISecureStorageService
{
    void SaveSecret(string key, string secret);
    string? GetSecret(string key);
    void DeleteSecret(string key);
}
