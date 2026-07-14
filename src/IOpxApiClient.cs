// Copyright (c) 2026 - opx
namespace Opx.Api.Client;

public interface IOpxApiClient
{
	Task<OpxApiResult<T>> SendAsync<T>(HttpMethod method, string path, OpxApiRequest? request = null);
	Task<OpxApiResult<T>> SendAsync<T>(HttpMethod method, string path, OpxApiRequest? request, CancellationToken cancellationToken);
	OpxApiResult<T> Send<T>(HttpMethod method, string path, OpxApiRequest? request = null);
	Task<OpxApiResult<T>> GetAsync<T>(string path, OpxApiRequest? request = null);
	Task<OpxApiResult<T>> GetAsync<T>(string path, OpxApiRequest? request, CancellationToken cancellationToken);
	OpxApiResult<T> Get<T>(string path, OpxApiRequest? request = null);
	Task<OpxApiResult<T>> PostAsync<T>(string path, OpxApiRequest? request = null);
	Task<OpxApiResult<T>> PostAsync<T>(string path, OpxApiRequest? request, CancellationToken cancellationToken);
	OpxApiResult<T> Post<T>(string path, OpxApiRequest? request = null);
	Task<OpxApiResult<T>> PutAsync<T>(string path, OpxApiRequest? request = null);
	Task<OpxApiResult<T>> PutAsync<T>(string path, OpxApiRequest? request, CancellationToken cancellationToken);
	OpxApiResult<T> Put<T>(string path, OpxApiRequest? request = null);
	Task<OpxApiResult<T>> DeleteAsync<T>(string path, OpxApiRequest? request = null);
	Task<OpxApiResult<T>> DeleteAsync<T>(string path, OpxApiRequest? request, CancellationToken cancellationToken);
	OpxApiResult<T> Delete<T>(string path, OpxApiRequest? request = null);
	Task<OpxDownloadResult> DownloadAsync(
		string path,
		Stream destination,
		OpxApiRequest? request = null,
		IProgress<OpxDownloadProgress>? progress = null,
		CancellationToken cancellationToken = default);
}
