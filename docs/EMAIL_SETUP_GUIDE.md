# Email Forwarding Integration Guide

Notify Hub delivers captured notifications to any email address using standard SMTP protocols with modern HTML formatting.

---

## 1. Supported Email Providers & SMTP Settings

Notify Hub works with any standard SMTP server. Below are common configuration settings:

### Gmail
- **SMTP Host:** `smtp.gmail.com`
- **Port:** `587` (TLS) or `465` (SSL)
- **Use SSL / TLS:** `Enabled`
- **Username:** Your full Gmail address (e.g. `yourname@gmail.com`)
- **Password:** **Google App Password** *(Requires 2-Step Verification enabled in Google Account > Security > App passwords)*.
- **From Address:** Your Gmail address

### Outlook / Office 365
- **SMTP Host:** `smtp.office365.com`
- **Port:** `587`
- **Use SSL / TLS:** `Enabled`
- **Username:** Your full Outlook/Microsoft 365 email
- **Password:** Your Microsoft account password or App Password

### Custom / Corporate SMTP
- Consult your system administrator for the SMTP host, port, and authentication credentials.

---

## 2. Configure Notify Hub

1. Open **Notify Hub** and click **⚙️ Settings**.
2. Select **📧 Email Forwarding** from the sidebar.
3. Check **Enable Instant Email Forwarding**.
4. Fill in:
   - **SMTP Host:** e.g. `smtp.gmail.com`
   - **Port:** `587`
   - **Use SSL / TLS:** Checked
   - **SMTP Username:** Your email address
   - **SMTP Password:** Stored securely in Windows Credential Manager
   - **From Address:** e.g. `notifyhub@yourdomain.com` or your email address
   - **Destination Email Address:** Where notifications should be sent
5. Click **Send Test Email**.
   Notify Hub will connect, authenticate, and send a test email.
6. Verify receipt in your inbox and click **Save All Settings**.

---

## 3. Email Template & Details

Each forwarded email is sent as a responsive HTML card containing:
- **Brand Header:** 🔔 Notify Hub Alert with priority pill (Urgent, High, Normal, Low)
- **Source Application:** e.g. `Slack`, `WhatsApp`, `Bank of America`
- **Category Badge:** e.g. `Finance & Banking`, `Messages & Communication`
- **Timestamp:** Exact received time formatted to the second
- **Status:** Initial tracking status (`Pending`)
- **Complete Content:** Formatted title and multi-line body content
