//! Talks to the license server (a Kite webhook). The URL is baked in at build time from NEOX_ACTIVATION_URL.

use crate::runner::{powershell, Runner};
use neox_core::activation::{parse_reply, Reply};

pub fn server_url() -> Option<&'static str> {
    let u = option_env!("NEOX_ACTIVATION_URL")?.trim();
    // Only a plain https URL made of safe characters is ever used: it goes into a PowerShell string.
    let ok = u.starts_with("https://") && u.len() < 400 && u.bytes().all(|b| b.is_ascii_alphanumeric() || b"-._~:/?#[]@!$&()*+,;=%".contains(&b));
    ok.then_some(u)
}

/// Builds the PowerShell that posts the request. `code` and `hwid` must already be validated (letters, digits, dashes).
pub fn script(url: &str, code: &str, hwid: &str) -> String {
    format!("$ErrorActionPreference='Stop'; try {{ $b='{{\"action\":\"verify\",\"code\":\"{code}\",\"hwid\":\"{hwid}\"}}'; \
Invoke-RestMethod -Uri '{url}' -Method Post -ContentType 'application/json' -Body $b -TimeoutSec 20 | ConvertTo-Json -Compress }} catch {{ 'NETERR' }}")
}

fn safe(s: &str) -> bool { !s.is_empty() && s.len() < 40 && s.bytes().all(|b| b.is_ascii_alphanumeric() || b == b'-') }

pub fn verify(run: &dyn Runner, code: &str, hwid: &str) -> Reply {
    let Some(url) = server_url() else { return Reply::Unreachable };
    if !safe(code) || !safe(hwid) { return Reply::Denied("Invalid code format.".into()); }
    let r = run.run(&powershell(&script(url, code, hwid)), 35);
    parse_reply(&r.output)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::runner::fake::Script;

    #[test]
    fn script_contains_the_request_and_no_shell_metacharacters_from_input() {
        let s = script("https://example.com/h", "NEOX-AAAA-BBBB-CCCC", "NEOX-1111-2222-3333-4444");
        assert!(s.contains("\"code\":\"NEOX-AAAA-BBBB-CCCC\"") && s.contains("-Uri 'https://example.com/h'"));
    }
    #[test]
    fn unsafe_input_is_refused_before_any_process_runs() {
        assert!(!safe("a'; calc; '") && !safe("") && safe("NEOX-AB12-CD34-EF56"));
    }
    #[test]
    fn without_a_build_time_url_the_server_is_unreachable() {
        if server_url().is_none() { assert_eq!(verify(&Script::new(), "NEOX-AAAA-BBBB-CCCC", "NEOX-1111-2222-3333-4444"), Reply::Unreachable); }
    }
}
