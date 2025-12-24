/*
 * transfer.h - File upload/download via TCP
 */

#ifndef TRANSFER_H
#define TRANSFER_H

#include "claude.h"

/*
 * Download a file from the proxy server.
 *
 * remote_path: Path on the proxy server
 * local_path:  Local path to save the file
 *
 * Returns 0 on success, -1 on failure.
 */
int transfer_download(const char *remote_path, const char *local_path);

/*
 * Upload a file to the proxy server.
 *
 * local_path:  Local file to upload
 * remote_path: Destination path on the proxy server
 *
 * Returns 0 on success, -1 on failure.
 */
int transfer_upload(const char *local_path, const char *remote_path);

#endif /* TRANSFER_H */
