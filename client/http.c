/*
 * http.c - HTTP client
 */

#include "http.h"

const char *http_error_string(HttpResult code)
{
    switch (code) {
    case HTTP_OK:
        return "Success";
    case HTTP_ERR_SOCKET:
        return "Could not create socket";
    case HTTP_ERR_CONNECT:
        return "Could not connect to server";
    case HTTP_ERR_OVERFLOW:
        return "Request too large";
    case HTTP_ERR_SEND:
        return "Failed to send request";
    case HTTP_ERR_TIMEOUT:
        return "Request timed out";
    case HTTP_ERR_NO_BODY:
        return "No response body";
    case HTTP_ERR_SERVER:
        return "Server returned error status";
    case HTTP_ERR_TRUNCATED:
        return "Response truncated";
    case HTTP_ERR_RESPONSE_TOO_LARGE:
        return "Response Content-Length exceeds buffer size";
    default:
        return "Unknown error";
    }
}

static int parse_http_status(const char *buffer)
{
    int status = 0;
    if (strncmp(buffer, "HTTP/", 5) == 0) {
        const char *p = strchr(buffer, ' ');
        if (p) {
            status = atoi(p + 1);
        }
    }
    return status;
}

static int parse_content_length(const char *buffer)
{
    const char *cl = strstr(buffer, "Content-Length:");
    if (!cl) {
        cl = strstr(buffer, "content-length:");
    }
    if (cl) {
        return atoi(cl + 15);
    }
    return -1;
}

HttpResult http_request(const char *method, const char *path, const char *body,
                        char *response, size_t resp_size)
{
    SOCKET sock;
    struct sockaddr_in server;
    char buffer[BUFFER_SIZE];
    int total = 0;
    int body_len;
    int req_len;
    char *body_start;
    size_t req_capacity;
    char *request;

    response[0] = '\0';

    sock = socket(AF_INET, SOCK_STREAM, 0);
    if (sock == INVALID_SOCKET) {
        return HTTP_ERR_SOCKET;
    }

    server.sin_family = AF_INET;
    server.sin_port = htons((unsigned short)g_state.server_port);
    server.sin_addr.s_addr = inet_addr(g_state.server_ip);

    {
        u_long nonblocking = 1;
        u_long blocking = 0;
        fd_set writefds;
        struct timeval tv;
        int connect_result;
        int so_error;
        int so_len = sizeof(so_error);

        ioctlsocket(sock, FIONBIO, &nonblocking);

        connect_result = connect(sock, (struct sockaddr *)&server, sizeof(server));

        if (connect_result < 0 && WSAGetLastError() != WSAEWOULDBLOCK) {
            closesocket(sock);
            return HTTP_ERR_CONNECT;
        }

        if (connect_result != 0) {
            FD_ZERO(&writefds);
            FD_SET(sock, &writefds);
            tv.tv_sec = HTTP_TIMEOUT_SEC;
            tv.tv_usec = 0;

            if (select((int)sock + 1, NULL, &writefds, NULL, &tv) <= 0) {
                closesocket(sock);
                return HTTP_ERR_CONNECT;
            }

            if (getsockopt(sock, SOL_SOCKET, SO_ERROR, (char *)&so_error,
                           &so_len) < 0 ||
                so_error != 0) {
                closesocket(sock);
                return HTTP_ERR_CONNECT;
            }
        }

        ioctlsocket(sock, FIONBIO, &blocking);
    }

    body_len = (body != NULL) ? (int)strlen(body) : 0;
    req_capacity = (size_t)body_len + strlen(path) + strlen(method) +
                   strlen(g_state.server_ip) + 256;
    if (req_capacity < BUFFER_SIZE) {
        req_capacity = BUFFER_SIZE;
    }

    request = (char *)malloc(req_capacity);
    if (!request) {
        closesocket(sock);
        return HTTP_ERR_OVERFLOW;
    }

    if (body_len > 0) {
        req_len = snprintf(request, req_capacity,
                           "%s %s HTTP/1.1\r\n"
                           "Host: %s:%d\r\n"
                           "X-API-Key: %s\r\n"
                           "Content-Type: application/json\r\n"
                           "Content-Length: %d\r\n"
                           "Connection: close\r\n"
                           "\r\n"
                           "%s",
                           method, path, g_state.server_ip, g_state.server_port,
                           API_KEY, body_len, body);
    } else {
        req_len = snprintf(request, req_capacity,
                           "%s %s HTTP/1.1\r\n"
                           "Host: %s:%d\r\n"
                           "X-API-Key: %s\r\n"
                           "Connection: close\r\n"
                           "\r\n",
                           method, path, g_state.server_ip, g_state.server_port,
                           API_KEY);
    }

    if (req_len < 0 || (size_t)req_len >= req_capacity) {
        free(request);
        closesocket(sock);
        return HTTP_ERR_OVERFLOW;
    }

    if (send(sock, request, req_len, 0) < 0) {
        free(request);
        closesocket(sock);
        return HTTP_ERR_SEND;
    }

    free(request);
    memset(buffer, 0, sizeof(buffer));

    while ((size_t)total < sizeof(buffer) - 1) {
        fd_set readfds;
        struct timeval tv;
        int received;

        FD_ZERO(&readfds);
        FD_SET(sock, &readfds);
        tv.tv_sec = HTTP_TIMEOUT_SEC;
        tv.tv_usec = 0;

        if (select((int)sock + 1, &readfds, NULL, NULL, &tv) <= 0) {
            break;
        }

        received = recv(sock, buffer + total, sizeof(buffer) - total - 1, 0);
        if (received <= 0) {
            break;
        }
        total += received;
    }
    buffer[total] = '\0';

    closesocket(sock);

    if (total == 0) {
        return HTTP_ERR_TIMEOUT;
    }

    {
        int status = parse_http_status(buffer);
        if (status < 200 || status >= 300) {
            return HTTP_ERR_SERVER;
        }
    }

    body_start = strstr(buffer, "\r\n\r\n");
    if (body_start) {
        int content_length;
        int body_len;

        body_start += 4;
        body_len = total - (int)(body_start - buffer);

        content_length = parse_content_length(buffer);

        if (content_length > 0 && (size_t)content_length >= resp_size) {
            return HTTP_ERR_RESPONSE_TOO_LARGE;
        }

        if (content_length > 0 && body_len < content_length) {
            return HTTP_ERR_TRUNCATED;
        }

        if (content_length <= 0 && (size_t)total >= sizeof(buffer) - 1) {
            return HTTP_ERR_TRUNCATED;
        }

        strncpy(response, body_start, resp_size - 1);
        response[resp_size - 1] = '\0';
        return HTTP_OK;
    }

    return HTTP_ERR_NO_BODY;
}
