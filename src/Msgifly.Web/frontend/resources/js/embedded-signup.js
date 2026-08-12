// WhatsApp Embedded Signup — lets the admin connect a Workspace's WhatsApp Business Account by
// logging into Facebook and picking/creating a WABA + phone number inside Meta's own popup,
// instead of pasting a System User token by hand (see Waba/Index.cshtml's "Connect via Facebook"
// button, wired up from Save Scripts section).
//
// Two independent pieces of information come back from the popup and have to be stitched
// together before we have anything usable:
//   1. FB.login()'s own callback hands back an OAuth authorization `code` (via authResponse).
//   2. Meta separately posts a `window.postMessage` event (type: "WA_EMBEDDED_SIGNUP") carrying
//      which WABA/phone number the user actually chose inside the flow — the code alone doesn't
//      say that. The postMessage listener below caches the most recent FINISH payload; the login
//      callback reads it once `code` arrives (they typically land within milliseconds of each
//      other, but we don't assume an order).

let fbSdkLoadPromise = null;
let lastSignupData = null;

function loadFacebookSdk(appId, apiVersion) {
  if (fbSdkLoadPromise) return fbSdkLoadPromise;

  fbSdkLoadPromise = new Promise((resolve) => {
    window.fbAsyncInit = function () {
      window.FB.init({ appId, autoLogAppEvents: true, xfbml: false, version: apiVersion });
      resolve();
    };

    if (document.getElementById('facebook-jssdk')) {
      resolve();
      return;
    }

    const script = document.createElement('script');
    script.id = 'facebook-jssdk';
    script.src = 'https://connect.facebook.net/en_US/sdk.js';
    document.body.appendChild(script);
  });

  return fbSdkLoadPromise;
}

window.addEventListener('message', (event) => {
  if (event.origin !== 'https://www.facebook.com' && event.origin !== 'https://web.facebook.com') {
    return;
  }

  let data;
  try {
    data = typeof event.data === 'string' ? JSON.parse(event.data) : event.data;
  } catch {
    return;
  }

  if (data?.type === 'WA_EMBEDDED_SIGNUP' && data.event === 'FINISH') {
    lastSignupData = data.data || {};
  }
});

/**
 * @param {{configId: string, appId: string, apiVersion: string, onStatus?: (msg: string) => void}} options
 */
window.startEmbeddedSignup = async function startEmbeddedSignup(options) {
  const { configId, appId, apiVersion, onStatus } = options;
  onStatus?.('Loading Facebook…');
  await loadFacebookSdk(appId, apiVersion);

  lastSignupData = null;
  onStatus?.('Waiting for the Facebook signup window…');

  window.FB.login(
    (response) => {
      const code = response?.authResponse?.code;
      if (!code) {
        onStatus?.(response?.status === 'not_authorized' ? 'Cancelled.' : 'Facebook signup did not return an authorization code.');
        return;
      }

      const wabaId = lastSignupData?.waba_id;
      if (!wabaId) {
        onStatus?.('Facebook did not report which WhatsApp Business Account was selected — try again.');
        return;
      }

      onStatus?.('Connecting…');
      document.getElementById('esCode').value = code;
      document.getElementById('esWabaId').value = wabaId;
      document.getElementById('esPhoneNumberId').value = lastSignupData?.phone_number_id || '';
      document.getElementById('embeddedSignupForm').submit();
    },
    {
      config_id: configId,
      response_type: 'code',
      override_default_response_type: true,
      extras: { setup: {}, featureType: '', sessionInfoVersion: '3' },
    }
  );
};
