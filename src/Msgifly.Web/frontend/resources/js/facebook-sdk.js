// Shared Facebook JS SDK loader — used by both embedded-signup.js (WhatsApp Embedded Signup) and
// lead-ads.js (Facebook Page login for Lead Ads). Both features need the same window.FB, so this
// lives in one place: loading the SDK twice would fight over window.fbAsyncInit and risk one
// caller resolving before FB.init() actually ran.

let fbSdkLoadPromise = null;

export function loadFacebookSdk(appId, apiVersion) {
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
