"""Apply only the reviewed logout UI fix. No database changes or backups."""
from pathlib import Path
import base64, subprocess, sys
if sys.argv[1:] != ['--apply']:
    raise SystemExit('Run with --apply to update the frontend logout handling.')
root = Path.home()/'ASTRA-Frontend-latest'
page = root/'src/pages/service-desk.tsx'
helper = root/'src/pages/auth-logout.ts'
text = page.read_text().replace('\r\n','\n')
old = """  async function logout(csrfToken: string) {
    const response = await fetch('/api/auth/logout', {
      method: 'POST',
      headers: { 'x-csrf-token': csrfToken },
    });
    if (response.ok) setState({ kind: 'signed-out' });
    else setState({ kind: 'error' });
  }"""
new = """  async function logout(csrfToken: string) {
    setSignInError('');
    try {
      await signOutSession(csrfToken);
      setState({ kind: 'signed-out' });
      router.replace('/');
    } catch (error) {
      setSignInError(error instanceof Error ? error.message : 'Sign out could not be completed. Please try again.');
    }
  }"""
previous = new.replace("      await signOutSession(csrfToken);", "      await signOutSession(csrfToken);\n      setPassword('');")
if previous in text and text.count(previous) == 1:
    text = text.replace(previous,new,1)
elif old in text and text.count(old) == 1:
    text = text.replace(old,new,1)
elif new not in text:
    raise SystemExit('STOP: Ubuntu logout code differs. No files changed; send the current logout function.')
import_line = "import { signOutSession } from './auth-logout';"
if import_line not in text:
    anchor = "import UiIcon from './ui-icon';"
    if text.count(anchor) != 1:
        raise SystemExit('STOP: import anchor differs. No files changed.')
    text = text.replace(anchor,anchor+'\n'+import_line,1)
notice = """        {state.kind === 'signed-in' && signInError ? (
          <div role="alert" className="notice error">{signInError}</div>
        ) : null}"""
if notice not in text:
    anchor = '      <main className="mx-auto max-w-6xl px-6 py-16 md:py-20">'
    if text.count(anchor) != 1:
        raise SystemExit('STOP: workspace markup differs. No files changed.')
    text = text.replace(anchor,anchor+'\n'+notice,1)
helper_source = base64.b64decode('Ly8gQSA0MDEgbWVhbnMgdGhlIGxvY2FsIHNlc3Npb24gaXMgYWxyZWFkeSBnb25lLiBPdGhlciBmYWlsdXJlcyBtdXN0IG5vdCBwcmV0ZW5kCi8vIHRoYXQgbG9nb3V0IHN1Y2NlZWRlZCBvciByZXBsYWNlIHRoZSBlbnRpcmUgd29ya3NwYWNlIHdpdGggYW4gb3V0YWdlIHNjcmVlbi4KZXhwb3J0IGFzeW5jIGZ1bmN0aW9uIHNpZ25PdXRTZXNzaW9uKAogIGNzcmZUb2tlbjogc3RyaW5nLAogIHJlcXVlc3Q6IHR5cGVvZiBmZXRjaCA9IGZldGNoLAopOiBQcm9taXNlPHZvaWQ+IHsKICBjb25zdCBzZW5kID0gKHRva2VuOiBzdHJpbmcpID0+IHJlcXVlc3QoJy9hcGkvYXV0aC9sb2dvdXQnLCB7CiAgICBtZXRob2Q6ICdQT1NUJywKICAgIGNyZWRlbnRpYWxzOiAnc2FtZS1vcmlnaW4nLAogICAgY2FjaGU6ICduby1zdG9yZScsCiAgICBoZWFkZXJzOiB7ICd4LWNzcmYtdG9rZW4nOiB0b2tlbiB9LAogIH0pOwogIGxldCByZXNwb25zZSA9IGF3YWl0IHNlbmQoY3NyZlRva2VuKTsKICBpZiAocmVzcG9uc2Uub2sgfHwgcmVzcG9uc2Uuc3RhdHVzID09PSA0MDEpIHJldHVybjsKCiAgaWYgKHJlc3BvbnNlLnN0YXR1cyA9PT0gNDAzKSB7CiAgICBjb25zdCBzZXNzaW9uID0gYXdhaXQgcmVxdWVzdCgnL2FwaS9hdXRoL21lJywgewogICAgICBjcmVkZW50aWFsczogJ3NhbWUtb3JpZ2luJywgY2FjaGU6ICduby1zdG9yZScsCiAgICB9KTsKICAgIGlmIChzZXNzaW9uLnN0YXR1cyA9PT0gNDAxKSByZXR1cm47CiAgICBpZiAoc2Vzc2lvbi5vaykgewogICAgICBjb25zdCBjdXJyZW50ID0gYXdhaXQgc2Vzc2lvbi5qc29uKCkgYXMgeyBjc3JmVG9rZW4/OiB1bmtub3duIH07CiAgICAgIGlmICh0eXBlb2YgY3VycmVudC5jc3JmVG9rZW4gPT09ICdzdHJpbmcnICYmIGN1cnJlbnQuY3NyZlRva2VuICYmCiAgICAgICAgICBjdXJyZW50LmNzcmZUb2tlbiAhPT0gY3NyZlRva2VuKSB7CiAgICAgICAgcmVzcG9uc2UgPSBhd2FpdCBzZW5kKGN1cnJlbnQuY3NyZlRva2VuKTsKICAgICAgICBpZiAocmVzcG9uc2Uub2sgfHwgcmVzcG9uc2Uuc3RhdHVzID09PSA0MDEpIHJldHVybjsKICAgICAgfQogICAgfQogIH0KICB0aHJvdyBuZXcgRXJyb3IocmVzcG9uc2Uuc3RhdHVzID09PSA0MDMKICAgID8gJ1NpZ24gb3V0IHdhcyBibG9ja2VkIGJ5IGEgc2Vzc2lvbiBzZWN1cml0eSBjaGVjay4gWW91IGFyZSBzdGlsbCBzaWduZWQgaW4uIFJlZnJlc2ggdGhpcyBwYWdlIGFuZCB0cnkgYWdhaW4uJwogICAgOiAnU2lnbiBvdXQgY291bGQgbm90IGJlIGNvbXBsZXRlZC4gWW91IG1heSBzdGlsbCBiZSBzaWduZWQgaW4uIFBsZWFzZSB0cnkgYWdhaW4uJyk7Cn0K').decode()
if helper.exists() and helper.read_text().replace('\r\n','\n') != helper_source.replace('\r\n','\n'):
    raise SystemExit('STOP: another logout helper is already present. No files changed.')
helper.write_text(helper_source)
page.write_text(text)
print('Logout UI corrected. No database or SSO security settings changed. No backups created.',flush=True)
subprocess.run(['npm','run','build'],cwd=root,check=True)
