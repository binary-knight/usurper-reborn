#!/bin/bash
# Usurper Reborn - Custom Login Shell (Option B: per-player accounts)
# Install: echo "/opt/usurper/usurper-shell.sh" >> /etc/shells
# Usage:   useradd -m -s /opt/usurper/usurper-shell.sh playerA
#
# For Option A (shared account), this file is NOT needed.
# The SSH ForceCommand in /etc/ssh/sshd_config.d/usurper.conf handles it.

exec /opt/usurper/UsurperReborn --online --user "$USER" --stdio 2>/var/usurper/logs/$USER.log
