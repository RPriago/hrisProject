import { clsx, type ClassValue } from 'clsx';
import { twMerge } from 'tailwind-merge';

export function cn(...inputs: ClassValue[]): string {
  return twMerge(clsx(inputs));
}

/**
 * Formats a numeric value or decimal string into standard Indonesian Rupiah (IDR).
 * Adheres to financial precision guidelines by avoiding binary floating point representation issues.
 */
export function formatCurrencyIdr(
  value: number | string | null | undefined,
  includeDecimals: boolean = false
): string {
  if (value === null || value === undefined || value === '') {
    return 'Rp 0';
  }

  const strVal = typeof value === 'number' ? value.toFixed(2) : String(value).trim();
  const isNegative = strVal.startsWith('-');
  const cleanStr = isNegative ? strVal.slice(1) : strVal;

  const parts = cleanStr.split('.');
  const integerPart = parts[0] || '0';
  const decimalPart = (parts[1] || '00').padEnd(2, '0').slice(0, 2);

  // Group integer part with thousand periods
  const formattedInteger = integerPart.replace(/\B(?=(\d{3})+(?!\d))/g, '.');

  let result = `Rp ${formattedInteger}`;
  if (includeDecimals) {
    result += `,${decimalPart}`;
  }

  return isNegative ? `-${result}` : result;
}

/**
 * Formats an ISO UTC timestamp into a human-readable localized string.
 * Default IANA time zone is 'Asia/Jakarta' (WIB).
 */
export function formatDateTime(
  isoUtc: string | Date | null | undefined,
  options: {
    timeZone?: string;
    includeTime?: boolean;
    locale?: string;
  } = {}
): string {
  if (!isoUtc) return '-';

  const date = typeof isoUtc === 'string' ? new Date(isoUtc) : isoUtc;
  if (isNaN(date.getTime())) return '-';

  const {
    timeZone = 'Asia/Jakarta',
    includeTime = true,
    locale = 'id-ID',
  } = options;

  const formatOptions: Intl.DateTimeFormatOptions = {
    timeZone,
    day: '2-digit',
    month: 'short',
    year: 'numeric',
  };

  if (includeTime) {
    formatOptions.hour = '2-digit';
    formatOptions.minute = '2-digit';
    formatOptions.hour12 = false;
  }

  const formatted = new Intl.DateTimeFormat(locale, formatOptions).format(date);
  return formatted;
}

/**
 * Formats a date string (YYYY-MM-DD) into Indonesian format, e.g. "09 Nov 2026".
 */
export function formatWorkDate(dateString: string | null | undefined): string {
  if (!dateString) return '-';
  const parts = dateString.split('-');
  if (parts.length !== 3) return dateString;

  const [year, month, day] = parts;
  const months = ['Jan', 'Feb', 'Mar', 'Apr', 'Mei', 'Jun', 'Jul', 'Agu', 'Sep', 'Okt', 'Nov', 'Des'];
  const monthIdx = parseInt(month, 10) - 1;
  const monthName = months[monthIdx] || month;

  return `${day} ${monthName} ${year}`;
}
