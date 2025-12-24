/*
 * http.c - HTTP client
 */

#ifndef HTTP_H
#define HTTP_H

#include "claude.h"

/*
 * Perform an HTTP request to the proxy server.
 *
 * method:    HTTP method (GET, POST, etc.)
 * path:      Request path (e.g., "/start")
 * body:      Request body (NULL for no body)
 * response:  Buffer to store response body
 * resp_size: Size of response buffer
 *
 * Returns: HTTP_OK on success, or an HttpResult error code.
 */
HttpResult http_request(const char *method, const char *path, const char *body,
                        char *response, size_t resp_size);

const char *http_error_string(HttpResult code);

#endif /* HTTP_H */
