import { delegate } from 'tippy.js';
import 'tippy.js/dist/tippy.css';

function initTippy() {
  const mainContainer = document.querySelector('#main');

  if (mainContainer) {
    delegate(mainContainer, {
      target: '[data-tippy-content]',
    });
  }
}

// Initialize on first load
document.addEventListener('DOMContentLoaded', () => {
  initTippy();
});

// Reinitialize tooltips after Livewire navigation
document.addEventListener('livewire:navigated', () => {
  initTippy();
});

// Also reapply tooltips when Livewire updates components
Livewire.hook('message.processed', () => {
  initTippy();
});
