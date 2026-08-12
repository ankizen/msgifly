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

document.addEventListener('DOMContentLoaded', () => {
  initTippy();
});
