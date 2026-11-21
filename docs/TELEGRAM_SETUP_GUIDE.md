# Telegram Forwarding Integration Guide

Notify Hub includes native, instant notification forwarding to any Telegram chat or Telegram group.

---

## 1. Create a Telegram Bot

1. Open the Telegram app on your computer or phone.
2. Search for `@BotFather` (the official Telegram bot for managing bots) and start a chat.
3. Send the command:
   ```text
   /newbot
   ```
4. Follow the prompts:
   - Provide a friendly name for your bot (e.g. `My Notification Bot`).
   - Provide a username ending in `bot` (e.g. `MyNotifyHub_bot`).
5. BotFather will reply with your **Bot Token**:
   ```text
   7182938475:AAH_example_token_abcdefg1234567
   ```
   *Keep this token private! Notify Hub will encrypt and store it using Windows Credential Manager.*

---

## 2. Obtain Your Telegram Chat ID

### Option A: Forwarding to Your Private Telegram Chat
1. In Telegram, start a conversation with `@userinfobot` or `@RawDataBot`.
2. It will immediately reply with your unique user ID (e.g. `987654321`).
3. **Important:** Send `/start` to your newly created bot so it has permission to message you.

### Option B: Forwarding to a Telegram Group / Channel
1. Create a new Telegram group (e.g. `Notifications Hub`).
2. Add your newly created bot to the group as an Administrator.
3. Send any test message in the group.
4. Obtain the group chat ID using `@RawDataBot` or by visiting:
   ```text
   https://api.telegram.org/bot<YOUR_BOT_TOKEN>/getUpdates
   ```
5. Group IDs start with a hyphen or `-100` (e.g. `-100192837465`).

---

## 3. Configure Notify Hub

1. Launch **Notify Hub** and click **⚙️ Settings** in the top-right corner.
2. Select **✈️ Telegram Forwarding** from the settings sidebar.
3. Check **Enable Instant Telegram Forwarding**.
4. Paste your **Telegram Bot Token**.
5. Paste your **Destination Chat ID**.
6. Click **Test Connection (getMe)**. You should see:
   `✅ Connection Successful!`
7. Click **Send Test Notification**. Your bot will immediately deliver a test alert to your Telegram chat:
   ```text
   🔔 Notify Hub — Test Connection
   Your Telegram forwarding integration is configured correctly and active!
   ```
8. Click **Save All Settings**.

---

## 4. Message Format & Behavior

Whenever Windows captures a new notification, Notify Hub formats and sends it instantly:

```text
🔔 NEW NOTIFICATION

📱 Source: WhatsApp
📌 Category: Messages & Communication
📝 Title: John Doe
💬 Content: Hey, are you free for a call?
🕒 Received: 16 November 2026, 09:30
📊 Status: Pending
```

- **Offline Behavior:** If the internet is temporarily disconnected, local SQLite capture continues without interruption. When connectivity is restored, failed deliveries can be retried automatically or with one click from the **Delivery Logs** tab.
