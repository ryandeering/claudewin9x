CC ?= gcc
WINDRES ?= windres
CFLAGS = -std=c99 -O3 -Wall -Wextra -DWINVER=0x0400 -D_WIN32_WINNT=0x0400 -I.
LDFLAGS = -static -lws2_32 -Wl,--subsystem,console:4.0

TARGET = ClaudeWin9xNt-Client.exe
RESOURCE = ClaudeWin9xNtClient.rc
RESOURCE_OBJ = ClaudeWin9xNtClient.res

# Source files
SOURCES = claude.c commands.c handlers.c http.c session.c transfer.c util.c
THIRD_PARTY = third_party/cJSON.c
LINT_SOURCES = $(SOURCES)

all: $(TARGET)

$(RESOURCE_OBJ): $(RESOURCE)
	$(WINDRES) $(RESOURCE) -O coff -o $(RESOURCE_OBJ)

# Build with resource file if windres is available
$(TARGET): $(SOURCES) $(THIRD_PARTY) $(if $(shell which $(WINDRES) 2>/dev/null),$(RESOURCE_OBJ),)
	$(CC) $(CFLAGS) $(SOURCES) $(THIRD_PARTY) $(if $(shell which $(WINDRES) 2>/dev/null),$(RESOURCE_OBJ),) -o $(TARGET) $(LDFLAGS)

lint:
	cppcheck --std=c99 --enable=warning,performance --error-exitcode=1 \
		--suppress=missingIncludeSystem --suppress=normalCheckLevelMaxBranches \
		--suppress=checkersReport $(LINT_SOURCES)

lint-all:
	cppcheck --std=c99 --enable=all --inconclusive \
		--suppress=missingIncludeSystem $(LINT_SOURCES)

clean:
	rm -f $(TARGET) $(RESOURCE_OBJ)

.PHONY: all clean lint lint-all
