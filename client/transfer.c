/*
 * transfer.c - File upload/download via TCP
 *
 *******************************************************************************
 *    Some reference taken from:
 *    Title: Beej's Guide to Network Programming
 *    Author: Brian "Beej Jorgensen" Hall
 *    Date: 19/12/25
 *    Availability: https://beej.us/guide/bgnet/html/split/client-server-background.html
 *
 *******************************************************************************
 */

#include "transfer.h"
#include "util.h"

static int read_line(SOCKET sock, char *buf, size_t bufsize)
{
    int pos = 0;

    while ((size_t)pos < bufsize - 1) {
        fd_set readfds;
        struct timeval tv;
        int received;

        FD_ZERO(&readfds);
        FD_SET(sock, &readfds);
        tv.tv_sec = TRANSFER_TIMEOUT_SEC;
        tv.tv_usec = 0;

        if (select((int)sock + 1, &readfds, NULL, NULL, &tv) <= 0) {
            return -1;
        }

        received = recv(sock, &buf[pos], 1, 0);
        if (received <= 0) {
            return -1;
        }

        if (buf[pos] == '\n') {
            buf[pos] = '\0';
            return pos;
        }
        pos++;
    }

    buf[pos] = '\0';
    return pos;
}

int transfer_download(const char *remote_path, const char *local_path)
{
    char request[512];
    char header[256];
    char buffer[4096];
    SOCKET sock;
    struct sockaddr_in server;
    FILE *fp;
    unsigned long total = 0;
    unsigned long file_size;
    char *endptr;

    printf("[Downloading %s -> %s]\n", remote_path, local_path);

    sock = socket(AF_INET, SOCK_STREAM, 0);
    if (sock == INVALID_SOCKET) {
        log_error("download", "Could not create socket");
        return -1;
    }

    server.sin_family = AF_INET;
    server.sin_addr.s_addr = inet_addr(g_state.server_ip);
    server.sin_port = htons(PORT_FILE_DOWNLOAD);

    if (connect(sock, (struct sockaddr *)&server, sizeof(server)) < 0) {
        log_error("download", "Could not connect to file server");
        closesocket(sock);
        return -1;
    }

    snprintf(request, sizeof(request), "%s\n%s\n", API_KEY, remote_path);
    if (send(sock, request, (int)strlen(request), 0) < 0) {
        log_error("download", "Failed to send request");
        closesocket(sock);
        return -1;
    }

    if (read_line(sock, header, sizeof(header)) < 0) {
        log_error("download", "Timeout waiting for server response");
        closesocket(sock);
        return -1;
    }

    if (strncmp(header, "ERROR ", 6) == 0) {
        log_error("download", header + 6);
        closesocket(sock);
        return -1;
    }

    if (strncmp(header, "OK ", 3) != 0) {
        log_error("download", "Unexpected response from server");
        closesocket(sock);
        return -1;
    }

    file_size = strtoul(header + 3, &endptr, 10);
    if (endptr == header + 3 || file_size == 0) {
        log_error("download", "Invalid file size");
        closesocket(sock);
        return -1;
    }

    printf("[File size: %lu bytes]\n", file_size);

    fp = fopen(local_path, "wb");
    if (!fp) {
        log_error("download", "Could not create local file");
        closesocket(sock);
        return -1;
    }

    while (total < file_size) {
        fd_set readfds;
        struct timeval tv;
        int received;
        size_t to_read = sizeof(buffer);

        if (file_size - total < to_read) {
            to_read = (size_t)(file_size - total);
        }

        FD_ZERO(&readfds);
        FD_SET(sock, &readfds);
        tv.tv_sec = TRANSFER_TIMEOUT_SEC;
        tv.tv_usec = 0;

        if (select((int)sock + 1, &readfds, NULL, NULL, &tv) <= 0) {
            log_error("download", "Timeout during transfer");
            break;
        }

        received = recv(sock, buffer, (int)to_read, 0);
        if (received <= 0) {
            break;
        }

        if (fwrite(buffer, 1, (size_t)received, fp) != (size_t)received) {
            log_error("download", "Failed to write to file");
            break;
        }
        total += (unsigned long)received;

        printf("\r[%lu / %lu bytes (%lu%%)]", total, file_size,
               total / (file_size / 100 + 1));
        fflush(stdout);
    }

    fclose(fp);
    closesocket(sock);

    if (total == file_size) {
        printf("\r[Downloaded %lu bytes to %s]              \n", total,
               local_path);
        return 0;
    }

    printf("\n[Warning: Incomplete transfer %lu / %lu bytes]\n", total,
           file_size);
    return -1;
}

int transfer_upload(const char *local_path, const char *remote_path)
{
    char header[512];
    char response[256];
    char buffer[4096];
    SOCKET sock;
    struct sockaddr_in server;
    FILE *fp;
    size_t bytes_read;
    unsigned long total = 0;
    unsigned long file_size;

    fp = fopen(local_path, "rb");
    if (!fp) {
        log_error("upload", "Could not open local file");
        return -1;
    }

    if (fseek(fp, 0, SEEK_END) != 0) {
        log_error("upload", "Could not seek to end of file");
        fclose(fp);
        return -1;
    }

    {
        long ftell_result = ftell(fp);
        if (ftell_result < 0) {
            log_error("upload", "Could not determine file size");
            fclose(fp);
            return -1;
        }
        file_size = (unsigned long)ftell_result;
    }

    if (fseek(fp, 0, SEEK_SET) != 0) {
        log_error("upload", "Could not seek to start of file");
        fclose(fp);
        return -1;
    }

    printf("[Uploading %s (%lu bytes) -> %s]\n", local_path, file_size,
           remote_path);

    sock = socket(AF_INET, SOCK_STREAM, 0);
    if (sock == INVALID_SOCKET) {
        log_error("upload", "Could not create socket");
        fclose(fp);
        return -1;
    }

    server.sin_family = AF_INET;
    server.sin_addr.s_addr = inet_addr(g_state.server_ip);
    server.sin_port = htons(PORT_FILE_UPLOAD);

    if (connect(sock, (struct sockaddr *)&server, sizeof(server)) < 0) {
        log_error("upload", "Could not connect to upload server");
        closesocket(sock);
        fclose(fp);
        return -1;
    }

    snprintf(header, sizeof(header), "%s\n%s\n%lu\n", API_KEY, remote_path, file_size);
    if (send(sock, header, (int)strlen(header), 0) < 0) {
        log_error("upload", "Failed to send header");
        closesocket(sock);
        fclose(fp);
        return -1;
    }

    while ((bytes_read = fread(buffer, 1, sizeof(buffer), fp)) > 0) {
        if (send(sock, buffer, (int)bytes_read, 0) < 0) {
            log_error("upload", "Failed to send data");
            fclose(fp);
            closesocket(sock);
            return -1;
        }
        total += (unsigned long)bytes_read;

        printf("\r[Sent %lu / %lu bytes (%lu%%)]", total, file_size,
               total / (file_size / 100 + 1));
        fflush(stdout);
    }

    fclose(fp);

    if (read_line(sock, response, sizeof(response)) < 0) {
        log_error("upload", "No response from server");
        closesocket(sock);
        return -1;
    }

    closesocket(sock);

    if (strncmp(response, "ERROR ", 6) == 0) {
        log_error("upload", response + 6);
        return -1;
    }

    if (strcmp(response, "OK") == 0) {
        printf("\r[Uploaded %lu bytes to %s]              \n", total,
               remote_path);
        return 0;
    }

    log_error("upload", "Unexpected response from server");
    return -1;
}
