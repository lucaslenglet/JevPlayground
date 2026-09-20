const $ = (sel, root = document) => root.querySelector(sel);
const STORAGE_KEY = 'jev-playground-form';

const els = {
  form: $('#form'),
  state: $('#state'),
  questions: $('#questions'),
  addQuestion: $('#add-question'),
  reset: $('#reset'),
  send: $('#send'),
  status: $('#status'),
  results: $('#results'),
  raw: $('#raw'),
  rawWrap: $('#raw-wrap'),
  config: $('#config'),
};

const EMPTY_RESULTS = 'Nothing yet. Fill in a context and some questions, then send.';

const DEFAULT_FORM = {
  state: 'Hi, my order arrived broken yesterday. I want a refund, this is the second time.',
  questions: [
    {
      id: 'intent',
      type: 'choice',
      instructions: 'What is the main intent of this message?',
      criteria: [
        { id: 'refund', desc: 'The customer wants their money back.' },
        { id: 'exchange', desc: 'The customer wants a replacement product.' },
        { id: 'information', desc: 'The customer is only asking a question.' },
      ],
    },
    {
      id: 'urgency',
      type: 'score',
      instructions: 'How urgent is this request?',
      criteria: [{ id: 'low' }, { id: 'medium' }, { id: 'high' }],
    },
    { id: 'unhappy', type: 'noul', instructions: 'Is the customer unhappy?', criteria: [] },
  ],
};

// --- Form ------------------------------------------------------------------

function addCriterion(card, value = { id: '', desc: '' }) {
  const node = $('#criterion-template').content.firstElementChild.cloneNode(true);
  $('.c-id', node).value = value.id ?? '';
  $('.c-desc', node).value = value.desc ?? '';
  $('.remove', node).addEventListener('click', () => {
    node.remove();
    save();
  });
  $('.criteria-list', card).append(node);
  syncCriteriaLabels(card);
  return node;
}

// The wording changes with the type: options for `choice`, levels for `score`.
function syncCriteriaLabels(card) {
  const type = $('.q-type', card).value;
  const criteria = $('.criteria', card);
  criteria.hidden = type === 'noul';
  $('.add-criterion', card).textContent = type === 'score' ? '+ Level' : '+ Option';
  const placeholder = type === 'score' ? 'level (lowest to highest)' : 'possible value';
  for (const row of criteria.querySelectorAll('.criterion')) {
    $('.c-id', row).placeholder = placeholder;
    $('.c-desc', row).hidden = type === 'score';
  }
}

function addQuestion(value = { id: '', type: 'choice', instructions: '', criteria: [] }) {
  const card = $('#question-template').content.firstElementChild.cloneNode(true);
  $('.q-id', card).value = value.id ?? '';
  $('.q-type', card).value = value.type ?? 'choice';
  $('.q-instructions', card).value = value.instructions ?? '';

  const criteria = value.criteria?.length ? value.criteria : [{ id: '' }, { id: '' }];
  for (const criterion of criteria) addCriterion(card, criterion);

  $('.q-type', card).addEventListener('change', () => {
    syncCriteriaLabels(card);
    save();
  });
  $('.add-criterion', card).addEventListener('click', () => {
    addCriterion(card).querySelector('.c-id').focus();
    save();
  });
  $('.question-head .remove', card).addEventListener('click', () => {
    card.remove();
    save();
  });

  els.questions.append(card);
  syncCriteriaLabels(card);
  return card;
}

function readForm() {
  return {
    state: els.state.value,
    questions: [...els.questions.querySelectorAll('.question')].map((card) => ({
      id: $('.q-id', card).value.trim(),
      type: $('.q-type', card).value,
      instructions: $('.q-instructions', card).value.trim(),
      criteria: [...card.querySelectorAll('.criterion')]
        .map((row) => ({ id: $('.c-id', row).value.trim(), desc: $('.c-desc', row).value.trim() }))
        .filter((c) => c.id !== ''),
    })),
  };
}

function writeForm(form) {
  els.state.value = form.state ?? '';
  els.questions.replaceChildren();
  for (const question of form.questions ?? []) addQuestion(question);
}

function save() {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(readForm()));
  } catch {
    /* private mode, storage unavailable: harmless */
  }
}

function load() {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (raw) return JSON.parse(raw);
  } catch {
    /* same */
  }
  return DEFAULT_FORM;
}

// --- Payload ---------------------------------------------------------------

function buildPayload(form) {
  const questions = {};
  const seen = new Set();

  for (const question of form.questions) {
    if (!question.id) throw new Error('Every question needs an identifier.');
    if (seen.has(question.id)) throw new Error(`Duplicate question identifier: “${question.id}”.`);
    seen.add(question.id);
    if (!question.instructions) throw new Error(`Question “${question.id}” has no instructions.`);

    if (question.type === 'choice') {
      if (question.criteria.length < 2) throw new Error(`Question “${question.id}” needs at least 2 options.`);
      questions[question.id] = {
        type: 'choice',
        instructions: question.instructions,
        criteria: Object.fromEntries(question.criteria.map((c) => [c.id, c.desc || null])),
      };
    } else if (question.type === 'score') {
      if (question.criteria.length < 2) throw new Error(`Question “${question.id}” needs at least 2 levels.`);
      questions[question.id] = {
        type: 'score',
        instructions: question.instructions,
        criteria: question.criteria.map((c) => c.id),
      };
    } else {
      questions[question.id] = { type: 'noul', instructions: question.instructions };
    }
  }

  if (!Object.keys(questions).length) throw new Error('Add at least one question.');
  if (!form.state.trim()) throw new Error('The context is empty.');
  return { state: form.state, questions };
}

// --- Answers ---------------------------------------------------------------

const pct = (value) => `${(value * 100).toFixed(1)}%`;

function bars(entries, topKey) {
  const list = document.createElement('div');
  list.className = 'bars';
  for (const [label, value] of entries.sort((a, b) => b[1] - a[1])) {
    const row = document.createElement('div');
    row.className = `bar${label === topKey ? ' is-top' : ''}`;
    row.innerHTML = `
      <span class="bar-label" title="${label}">${label}</span>
      <span class="bar-track"><span class="bar-fill"></span></span>
      <span class="bar-value">${pct(value)}</span>`;
    $('.bar-fill', row).style.width = `${Math.max(value * 100, 1)}%`;
    list.append(row);
  }
  return list;
}

function renderAnswer(id, answer, question) {
  const block = document.createElement('div');
  block.className = 'answer';

  let value = '';
  let confidence = '';
  let distribution = null;

  if (answer.type === 'choice') {
    value = answer.choice;
    confidence = `confidence ${pct(answer.confidence ?? 0)}`;
    distribution = bars(Object.entries(answer.probabilities ?? {}), answer.choice);
  } else if (answer.type === 'score') {
    const legend = answer.legend ?? {};
    const nearest = legend[String(Math.round(answer.score ?? 0))] ?? '';
    value = `${(answer.score ?? 0).toFixed(2)}${nearest ? ` — ${nearest}` : ''}`;
    confidence = `confidence ${pct(answer.confidence ?? 0)}`;
    distribution = bars(
      Object.entries(answer.probabilities ?? {}).map(([i, p]) => [legend[i] ?? i, p]),
      nearest,
    );
  } else {
    value = pct(answer.noul ?? 0);
    confidence = 'likelihood of “true”';
    distribution = bars([['true', answer.noul ?? 0], ['false', 1 - (answer.noul ?? 0)]], 'true');
  }

  block.innerHTML = `
    <div class="answer-head">
      <span class="answer-id">${id}</span>
      <span class="answer-value"></span>
      <span class="answer-conf">${confidence}</span>
    </div>
    <p class="answer-instructions"></p>`;
  $('.answer-value', block).textContent = value;
  $('.answer-instructions', block).textContent = question?.instructions ?? '';
  block.append(distribution);
  return block;
}

function renderResults(data) {
  const answers = data.response?.answers ?? {};
  els.results.classList.remove('empty');
  els.results.replaceChildren();
  for (const [id, answer] of Object.entries(answers)) {
    els.results.append(renderAnswer(id, answer, data.request?.questions?.[id]));
  }
  els.raw.textContent = JSON.stringify(data.response, null, 2);
  els.rawWrap.hidden = false;

  const usage = data.response?.usage;
  const count = Object.keys(answers).length;
  els.status.textContent = `${count} answer${count === 1 ? '' : 's'} in ${data.latencyMs} ms${
    usage ? ` · ${usage.input_tokens} input tokens` : ''
  }`;
}

function fail(message) {
  els.status.textContent = message;
  els.status.classList.add('error');
}

// --- Events ----------------------------------------------------------------

els.addQuestion.addEventListener('click', () => {
  addQuestion().querySelector('.q-id').focus();
  save();
});

els.reset.addEventListener('click', () => {
  writeForm(DEFAULT_FORM);
  save();
  els.results.classList.add('empty');
  els.results.textContent = EMPTY_RESULTS;
  els.rawWrap.hidden = true;
  els.status.textContent = '';
});

els.form.addEventListener('input', save);

els.form.addEventListener('submit', async (event) => {
  event.preventDefault();
  els.status.classList.remove('error');

  let payload;
  try {
    payload = buildPayload(readForm());
  } catch (error) {
    return fail(error.message);
  }

  els.send.disabled = true;
  els.status.textContent = 'Sending…';
  try {
    const res = await fetch('/api/classify', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    });
    const data = await res.json();
    if (!res.ok) {
      els.raw.textContent = JSON.stringify(data, null, 2);
      els.rawWrap.hidden = false;
      return fail(data.error ?? `HTTP error ${res.status}`);
    }
    renderResults(data);
  } catch (error) {
    fail(`Request failed: ${error.message}`);
  } finally {
    els.send.disabled = false;
  }
});

fetch('/api/config')
  .then((res) => res.json())
  .then((config) => {
    els.config.innerHTML = `Provider <code>${config.provider}</code> · model <code>${config.model}</code> · ${
      config.mock ? 'simulated answers, no network call' : `endpoint <code>${config.endpoint}</code>`
    }`;
  })
  .catch(() => {
    els.config.textContent = 'Configuration unavailable.';
  });

writeForm(load());
