// Facebook Page login for Lead Ads sync (Admin/LeadAds/Index.cshtml) — a plain Facebook Login,
// not WhatsApp's Embedded Signup: it needs pages_show_list/leads_retrieval scopes that aren't
// part of an Embedded Signup configuration's permission grant, so this asks for its own scopes
// directly and gets back a normal user access token (not an authorization code — no server-side
// exchange needed here, unlike embedded-signup.js).

import { loadFacebookSdk } from './facebook-sdk';

/**
 * @param {{appId: string, apiVersion: string, onStatus?: (msg: string) => void, onToken: (token: string) => void}} options
 */
window.startFacebookPageLogin = async function startFacebookPageLogin(options) {
  const { appId, apiVersion, onStatus, onToken } = options;
  if (!appId) {
    onStatus?.('Register your Meta App ID first (Connect WABA page, step 1).');
    return;
  }

  onStatus?.('Loading Facebook…');
  await loadFacebookSdk(appId, apiVersion);

  onStatus?.('Waiting for the Facebook login window…');
  window.FB.login(
    (response) => {
      const token = response?.authResponse?.accessToken;
      if (!token) {
        onStatus?.(response?.status === 'not_authorized' ? 'Cancelled.' : 'Facebook login did not return an access token.');
        return;
      }

      onToken(token);
    },
    // leadgen_forms (listing a Page's Instant Forms) 400s with "(#200) Requires
    // pages_manage_ads permission" without it — pages_manage_metadata/leads_retrieval alone
    // are enough to list Pages and pull leads from a form already known, but not to discover
    // the form list itself. ads_management is requested alongside it since Meta's own
    // integration guides for this endpoint bundle the two together.
    { scope: 'pages_show_list,leads_retrieval,pages_manage_metadata,pages_manage_ads,ads_management' }
  );
};
