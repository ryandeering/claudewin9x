/*
 * handlers.h - Tool approval, file operation, and command handlers
 */

#ifndef HANDLERS_H
#define HANDLERS_H

#include "claude.h"

int poll_approval(void);
int process_approval(void);
int handle_approval(void);

int handle_fileop(void);

int handle_command(void);

#endif /* HANDLERS_H */
