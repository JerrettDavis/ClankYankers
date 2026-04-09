#!/bin/sh
set -eu

USERNAME="${SSH_TEST_USER:-clanky}"
PASSWORD="${SSH_TEST_PASSWORD:-clanky-password}"

if ! id "$USERNAME" >/dev/null 2>&1; then
  adduser -D -s /bin/bash "$USERNAME"
fi

echo "$USERNAME:$PASSWORD" | chpasswd

mkdir -p "/home/$USERNAME/.ssh"

if [ -f /test-auth/authorized_keys ]; then
  cp /test-auth/authorized_keys "/home/$USERNAME/.ssh/authorized_keys"
fi

if [ -f /test-auth/trusted_user_ca_keys.pub ]; then
  cp /test-auth/trusted_user_ca_keys.pub /etc/ssh/trusted_user_ca_keys.pub
fi

chown -R "$USERNAME:$USERNAME" "/home/$USERNAME/.ssh"
chmod 700 "/home/$USERNAME/.ssh"

if [ -f "/home/$USERNAME/.ssh/authorized_keys" ]; then
  chmod 600 "/home/$USERNAME/.ssh/authorized_keys"
fi

exec /usr/sbin/sshd -D -e
