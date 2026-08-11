import axios from 'axios';
window.axios = axios;
window.axios.defaults.headers.common['X-Requested-With'] = 'XMLHttpRequest';

// NOTE: the original app's Pusher/Echo bootstrapping (meta-tag config, echoManager/pusherManager
// stores) is deferred to the Chat/Inbox phase, which replaces Pusher with SignalR — see
// WHATSMARK_MASTER_REFERENCE.md §12 ("Realtime chat").
