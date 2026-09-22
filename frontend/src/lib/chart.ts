/**
 * Chart colours, taken from the brand tokens in tailwind.config.js so charts match the UI.
 * Meaning is carried by colour only where it matters: red marks a value past its limit.
 */
export const chart = {
  grid: '#E3E9EE',
  axis: '#687C88',
  /** Primary series: the brand olive. */
  primary: '#63883C',
  /** Second series on the same chart: the record-ID blue. */
  secondary: '#5880E4',
  /** Limit lines and values beyond them. */
  limit: '#C4403C',
  /** Values inside their safe range. */
  inRange: '#A9B6C3',
  /** Missing data. */
  empty: '#E3E9EE',
  /** The shaded safe band. */
  band: '#EDF3E7',
} as const
