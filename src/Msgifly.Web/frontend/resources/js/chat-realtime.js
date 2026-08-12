import * as signalR from '@microsoft/signalr';

// Exposed globally so the Chat/Index.cshtml Alpine component (plain browser JS, not part of
// the Vite module graph) can open a hub connection without needing its own bundler entry.
window.createChatConnection = function () {
  return new signalR.HubConnectionBuilder()
    .withUrl('/hubs/chat')
    .withAutomaticReconnect()
    .build();
};
