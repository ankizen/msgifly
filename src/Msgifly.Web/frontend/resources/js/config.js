import intlTelInput from 'intl-tel-input';
import Quill from 'quill';
import 'quill/dist/quill.snow.css';
import flatpickr from 'flatpickr';
import 'flatpickr/dist/flatpickr.min.css'; // Import CSS
import DOMPurify from 'dompurify';
import { prettyPrintJson } from 'pretty-print-json';
import html2canvas from 'html2canvas';
// Quill Start
window.Quill = Quill;
window.showNotification = function (message, type = 'info') {
  const event = new CustomEvent('notify', {
    detail: [{ message: message, type: type }],
  });
  window.dispatchEvent(event);
};
//Quill end

// Globle Copy start
window.copyToClipboard = (text) => {
  if (!text) {
    showNotification('No text provided to copy', 'danger');
    return;
  }

  if (navigator.clipboard && navigator.clipboard.writeText) {
    // Clipboard API
    navigator.clipboard
      .writeText(text)
      .then(() => {
        showNotification('Text copied to clipboard!', 'success');
      })
      .catch(() => {
        showNotification('Failed to copy text', 'danger');
      });
  } else {
    // Fallback for unsupported browsers
    const tempTextArea = document.createElement('textarea');
    tempTextArea.value = text;
    document.body.appendChild(tempTextArea);
    tempTextArea.select();
    try {
      document.execCommand('copy');
      showNotification('Text copied to clipboard', 'success');
    } catch {
      showNotification('Failed to copy text', 'danger');
    } finally {
      document.body.removeChild(tempTextArea);
    }
  }
};
// Globle Copy end
document.addEventListener('DOMContentLoaded', function () {
  const input = document.querySelector('#phone');

  if (!input) return;

  const iti = intlTelInput(input, {
    strictMode: true,
    separateDialCode: false,
    autoHideDialCode: false,
    initialCountry: 'in',
    autoPlaceholder: 'off',
    nationalMode: false,
    loadUtils: () => import('intl-tel-input/build/js/utils.js'),
  });

  iti.promise
    .then(() => {
      input.addEventListener('blur', function () {
        const fullPhoneNumber = iti.getNumber(); // Get full phone number with country code

        // Only update if the value has changed
        if (input.value !== fullPhoneNumber) {
          input.value = fullPhoneNumber;
          input.dispatchEvent(new Event('input')); // Sync with Livewire
        }
      });
    })
    .catch((error) => {
      console.error('Error loading utils.js', error);
    });
});
//  flatePickrWithTime start
window.flatePickrWithTime = function () {
  const datePicker = flatpickr('#datepicker', {
    dateFormat: `${date_format} ${time_format}`,
    enableTime: true,
    allowInput: true,
    disableMobile: true,
    time_24hr: is24Hour,
  });
  datePicker.open();
  document.getElementById('datepicker').focus();
};

//  flatePickrWithTime start
window.flatePickrWithDate = function () {
  const datePicker = flatpickr('#datepicker', {
    dateFormat: date_format,
    enableTime: false,
    allowInput: true,
    disableMobile: true,
  });
  datePicker.open();
  document.getElementById('datepicker').focus();
};

//sanitize
window.sanitizeMessage = function (message) {
  return DOMPurify.sanitize(message, {
    USE_PROFILES: {
      html: true,
    },
  });
};
window.getObserver = function () {
  if (document.getElementById('power-grid-table-base') != null) {
    const observer = new MutationObserver(function (mutationsList, observer) {
      let parent = document.getElementById('power-grid-table-base');

      if (parent && parent.querySelector('.pg-filter-container')) {
        let firstChild = parent.querySelector('.pg-filter-container').children[0];

        if (firstChild && !firstChild.classList.contains('2xl:grid-cols-5')) {
          firstChild.classList.remove('2xl:grid-cols-6');
          firstChild.classList.add('2xl:grid-cols-5');
        }
      }
    });

    observer.observe(document.getElementById('power-grid-table-base'), {
      childList: true,
      subtree: true,
      attributes: true,
    });
  }
};
// pretty json
window.preety = function (data) {
  let { response, category, raw } = data;

  try {
    response = typeof response === 'string' ? JSON.parse(response) : response;
    category = typeof category === 'string' ? JSON.parse(category) : category;
    raw = typeof raw === 'string' ? JSON.parse(raw) : raw;
  } catch (error) {
    console.error('JSON Parsing Error:', error);
  }

  if (response && document.getElementById('json1')) {
    document.getElementById('json1').innerHTML = prettyPrintJson.toHtml(response);
  }
  if (raw && document.getElementById('datas')) {
    document.getElementById('datas').innerHTML = prettyPrintJson.toHtml(raw);
  }
  if (category && document.getElementById('raw')) {
    document.getElementById('raw').innerHTML = prettyPrintJson.toHtml(category);
  }
};

// capture screenshot
window.captureScreenshot = function (elementId, fileName = 'screenshot') {
  const captureArea = document.getElementById(elementId);

  if (!captureArea) {
    console.error(`Element with ID "${elementId}" not found.`);
    return;
  }

  const timestamp = new Date().toISOString().replace(/[:.]/g, '-');
  const dynamicFileName = `${fileName}-${timestamp}.png`;

  html2canvas(captureArea, {
    backgroundColor: null,
    scale: 2,
    letterRendering: true,
    height: captureArea.scrollHeight,
    useCORS: true,
  }).then((canvas) => {
    const link = document.createElement('a');
    link.href = canvas.toDataURL('image/png');
    link.download = dynamicFileName;
    link.click();
  });
};
