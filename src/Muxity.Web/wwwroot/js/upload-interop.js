/**
 * Upload interop for Blazor WASM.
 * Uses XMLHttpRequest to expose upload progress events to .NET.
 */
window.uploadHelper = {
    /**
     * Uploads a file via XHR multipart POST and reports progress to a .NET callback.
     * Returns the videoId on success, or throws on failure.
     */
    upload: function (apiBase, bearerToken, dotnetRef, formData) {
        return new Promise((resolve, reject) => {
            const xhr = new XMLHttpRequest();
            xhr.open('POST', `${apiBase}/videos/upload`);
            xhr.setRequestHeader('Authorization', `Bearer ${bearerToken}`);

            xhr.upload.onprogress = function (e) {
                if (e.lengthComputable) {
                    const pct = Math.round((e.loaded / e.total) * 100);
                    dotnetRef.invokeMethodAsync('OnUploadProgress', pct);
                }
            };

            xhr.onload = function () {
                if (xhr.status >= 200 && xhr.status < 300) {
                    try {
                        const result = JSON.parse(xhr.responseText);
                        resolve(result.videoId);
                    } catch {
                        reject(new Error('Invalid server response'));
                    }
                } else {
                    reject(new Error(`Upload failed: HTTP ${xhr.status}`));
                }
            };

            xhr.onerror = () => reject(new Error('Network error during upload'));
            xhr.onabort = () => reject(new Error('Upload aborted'));

            xhr.send(formData);
        });
    }
};
