import { describe, it, expect } from 'vitest';
import { render, screen } from '../test/test-utils';
import { SeverityBadge } from './SeverityBadge';

describe('SeverityBadge', () => {
  it('renders critical severity', () => {
    render(<SeverityBadge severity="critical" />);
    expect(screen.getByText('Critical')).toBeInTheDocument();
  });

  it('renders high severity', () => {
    render(<SeverityBadge severity="high" />);
    expect(screen.getByText('High')).toBeInTheDocument();
  });

  it('renders medium severity', () => {
    render(<SeverityBadge severity="medium" />);
    expect(screen.getByText('Medium')).toBeInTheDocument();
  });

  it('renders low severity', () => {
    render(<SeverityBadge severity="low" />);
    expect(screen.getByText('Low')).toBeInTheDocument();
  });

  it('renders info severity', () => {
    render(<SeverityBadge severity="info" />);
    expect(screen.getByText('Info')).toBeInTheDocument();
  });

  it('handles numeric severity values', () => {
    render(<SeverityBadge severity={0} />);
    expect(screen.getByText('Critical')).toBeInTheDocument();
  });
});
